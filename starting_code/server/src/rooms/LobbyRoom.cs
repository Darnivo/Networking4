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

		protected override void addMember(TcpMessageChannel pMember)
		{
			base.addMember(pMember);

			// Notify the client they joined the LOBBY_ROOM
			RoomJoinedEvent roomJoinedEvent = new RoomJoinedEvent();
			roomJoinedEvent.room = RoomJoinedEvent.Room.LOBBY_ROOM;
			pMember.SendMessage(roomJoinedEvent);

			// Announce new player in chat
			string playerName = _server.GetPlayerInfo(pMember).name;
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
			base.removeMember(pMember);
			_readyMembers.Remove(pMember);

			sendLobbyUpdateCount();
		}

		protected override void handleNetworkMessage(ASerializable pMessage, TcpMessageChannel pSender)
		{
			if (pMessage is ChatMessage chatMsg)
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
				msg.playerNames.Add(_server.GetPlayerInfo(member).name);
			}
			
			sendToAll(msg);
		}

	}
}
