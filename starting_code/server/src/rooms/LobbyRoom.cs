using shared;
using System.Collections.Generic;

namespace server
{
	/**
	 * The LobbyRoom is a little bit more extensive than the LoginRoom.
	 * In this room clients change their 'ready status'.
	 * If enough people are ready, they are automatically moved to the GameRoom to play a Game (assuming a game is not already in play).
	 */ 
	class LobbyRoom : SimpleRoom
	{
		//this list keeps tracks of which players are ready to play a game, this is a subset of the people in this room
		private List<TcpMessageChannel> _readyMembers = new List<TcpMessageChannel>();

		public LobbyRoom(TCPGameServer pOwner) : base(pOwner)
		{
		}

		public override void Update()
		{
			// Give newly joined players a grace period (3 seconds) before aggressive connection checks
			DateTime now = DateTime.Now;
			
			// Check for disconnections with grace period
			for (int i = _members.Count - 1; i >= 0; i--)
			{
				if (i < _members.Count) // Safety check
				{
					try
					{
						TcpMessageChannel member = _members[i];
						PlayerInfo playerInfo = _server.GetPlayerInfo(member);
						
						// Skip aggressive connection check if player joined recently
						bool inGracePeriod = (now - playerInfo.lastHeartbeatTime).TotalSeconds < 3;
						
						if (!inGracePeriod && !member.IsConnected())
						{
							Log.LogInfo("Detected disconnected client in lobby", this);
							removeAndCloseMember(member);
						}
					}
					catch (Exception e)
					{
						Log.LogInfo("Exception in lobby update: " + e.Message, this);
					}
				}
			}
			
			// Continue with normal update (heartbeats, message processing, etc.)
			base.Update();
		}

		protected override void addMember(TcpMessageChannel pMember)
		{
			// Check if member is already in the lobby - prevent duplication
			if (_members.Contains(pMember))
			{
				Log.LogInfo("Prevented duplicate member addition to lobby", this);
				return;
			}
			
			base.addMember(pMember);

			// Reset heartbeat timer when a player joins the lobby
			// This gives them a grace period before heartbeat checks
			PlayerInfo playerInfo = _server.GetPlayerInfo(pMember);
			playerInfo.lastHeartbeatTime = DateTime.Now;
			playerInfo.heartbeatPending = false;

			// Notify the client they joined the LOBBY_ROOM
			RoomJoinedEvent roomJoinedEvent = new RoomJoinedEvent();
			roomJoinedEvent.room = RoomJoinedEvent.Room.LOBBY_ROOM;
			pMember.SendMessage(roomJoinedEvent);

			// Announce new player in chat
			string playerName = playerInfo.name;
			ChatMessage joinMsg = new ChatMessage();
			joinMsg.sender = "[Server]";
			joinMsg.message = $"{playerName} has joined the lobby!";
			sendToAll(joinMsg);
			
			// Send updated lobby info to all clients
			sendLobbyUpdateCount();
		}

		/**
		 * Override removeMember so that our ready count and lobby count is updated (and sent to all clients)
		 * anytime we remove a member.
		 */
		protected override void removeMember(TcpMessageChannel pMember)
		{
			// Get the player's name before removing them
			string playerName = "";
			try {
				PlayerInfo info = _server.GetPlayerInfo(pMember);
				if (info != null) playerName = info.name;
			} catch {}
			
			// Call the base method to remove the member
			base.removeMember(pMember);
			
			// Remove from ready members list
			_readyMembers.Remove(pMember);
			
			// Send a message to all clients that a player has left
			if (!string.IsNullOrEmpty(playerName))
			{
				ChatMessage leaveMsg = new ChatMessage();
				leaveMsg.sender = "[Server]";
				leaveMsg.message = $"{playerName} has left the lobby!";
				sendToAll(leaveMsg);
			}
			
			// Update lobby information
			sendLobbyUpdateCount();
		}

		protected override void handleNetworkMessage(ASerializable pMessage, TcpMessageChannel pSender)
		{
			if (pMessage is HeartbeatResponse)
			{
				PlayerInfo playerInfo = _server.GetPlayerInfo(pSender);
				playerInfo.lastHeartbeatTime = DateTime.Now;
				playerInfo.heartbeatPending = false;
			}
			else if (pMessage is ChatMessage chatMsg)
			{
				// Set the sender name from the player info
				chatMsg.sender = _server.GetPlayerInfo(pSender).name;
				// Send the message to all clients
				sendToAll(chatMsg);
			}
			else if (pMessage is ChangeReadyStatusRequest)
			{
				handleReadyNotification(pMessage as ChangeReadyStatusRequest, pSender);
			}
		}

		private void handleReadyNotification(ChangeReadyStatusRequest pReadyNotification, TcpMessageChannel pSender)
		{
			//if the given client was not marked as ready yet, mark the client as ready
			if (pReadyNotification.ready)
			{
				if (!_readyMembers.Contains(pSender)) _readyMembers.Add(pSender);
			}
			else //if the client is no longer ready, unmark it as ready
			{
				_readyMembers.Remove(pSender);
			}

			//do we have enough people for a game and is there no game running yet?
			if (_readyMembers.Count >= 2)
			{
				TcpMessageChannel player1 = _readyMembers[0];
				TcpMessageChannel player2 = _readyMembers[1];
				
				// Ensure players are still connected
				if (!player1.IsConnected() || !player2.IsConnected())
				{
					Log.LogInfo("Players disconnected before game start - aborting", this);
					// Clean up disconnected players
					if (!player1.IsConnected()) removeAndCloseMember(player1);
					if (!player2.IsConnected()) removeAndCloseMember(player2);
					return;
				}
				
				// Remove from ready members
				_readyMembers.Remove(player1);
				_readyMembers.Remove(player2);
				
				// Remove from lobby
				removeMember(player1);
				removeMember(player2);
				
				// Create new game room and start game
				GameRoom gameRoom = _server.CreateNewGame();
                gameRoom.StartGame(player1, player2);
				
				// Log that a new game was created
				Log.LogInfo("New game started!", this, ConsoleColor.Green);
			}

			//(un)ready-ing / starting a game changes the lobby/ready count so send out an update
			//to all clients still in the lobby
			sendLobbyUpdateCount();
		}

		private void sendLobbyUpdateCount()
		{
			LobbyInfoUpdate msg = new LobbyInfoUpdate();
			msg.memberCount = memberCount;
			msg.readyCount = _readyMembers.Count;
			
			// Add all player names to the update
			msg.playerNames = new List<string>();
			foreach (TcpMessageChannel member in _members)
			{
				try {
					PlayerInfo info = _server.GetPlayerInfo(member);
					if (info != null && !string.IsNullOrEmpty(info.name)) {
						msg.playerNames.Add(info.name);
					}
				} catch {}
			}
			
			sendToAll(msg);
		}

		public override void CheckConnections()
		{
			// Force a thorough check of all members for disconnections
			for (int i = _members.Count - 1; i >= 0; i--)
			{
				if (i < _members.Count) // Safety check
				{
					try
					{
						TcpMessageChannel member = _members[i];
						if (!member.IsConnected())
						{
							Log.LogInfo("CheckConnections found disconnected client in lobby", this);
							removeAndCloseMember(member);
						}
					}
					catch (Exception e)
					{
						Log.LogInfo("Exception in CheckConnections: " + e.Message, this);
					}
				}
			}
		}
	}
}