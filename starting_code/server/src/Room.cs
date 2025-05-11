using shared;
using System;
using System.Collections.Generic;

namespace server
{
	/**
	 * Room is the abstract base class for all Rooms.
	 * 
	 * A room has a set of members and some base message processing functionality:
	 *	- addMember, removeMember, removeAndCloseMember, indexOfMember, memberCount
	 *	- safeForEach -> call a method on each member without crashing if a member leaves
	 *	- default administration: removing faulty member and processing incoming messages
	 *	
	 * Usage: subclass and override handleNetworkMessage
	 */
	abstract class Room
	{
		//allows all rooms to access the server they are a part of so they can request access to other rooms, client info etc
		protected TCPGameServer _server { private set; get; }
		//all members of this room (we identify them by their message channel)
		protected List<TcpMessageChannel> _members;

		/**
		 * Create a room with an empty member list and reference to the server instance they are a part of.
		 */
		protected Room (TCPGameServer pServer)
		{
			_server = pServer;
			_members = new List<TcpMessageChannel>();
		}

		protected virtual void addMember(TcpMessageChannel pMember)
		{
			Log.LogInfo("Client joined.", this);
			_members.Add(pMember);
		}

		protected virtual void removeMember(TcpMessageChannel pMember)
		{
			Log.LogInfo("Client left.", this);
			_members.Remove(pMember);
		}

		public int memberCount => _members.Count;
		
		protected int indexOfMember (TcpMessageChannel pMember)
		{
			return _members.IndexOf(pMember);
		}

		/**
		 * Should be called each server loop so that this room can do it's work.
		 */
		public virtual void Update()
		{
			sendHeartbeats();
			removeFaultyMembers();
			receiveAndProcessNetworkMessages();
		}

		/**
		 * Iterate over all members and remove the ones that have issues.
		 * Return true if any members were removed.
		 */
		protected bool removeFaultyMembers() 
		{
			bool anyRemoved = false;
			// Use a modified safeForEach that accumulates results
			for (int i = _members.Count - 1; i >= 0; i--)
			{
				if (i >= _members.Count) continue;
				if (checkFaultyMember(_members[i]))
					anyRemoved = true;
			}
			return anyRemoved;
		}

		/**
		* Iterates backwards through all members and calls the given method on each of them.
		* This basically allows you to process all clients, and optionally remove them 
		* without weird crashes due to collections being modified.
		* 
		* This can happen while looking for faulty clients, or when deciding to move a bunch 
		* of members to a different room, while you are still processing them.
		*/
		protected void safeForEach(Action<TcpMessageChannel> pMethod)
		{
			for (int i = _members.Count - 1; i >= 0; i--)
			{
				//skip any members that have been 'killed' in the mean time
				if (i >= _members.Count) continue;
				//call the method on any still existing member
				pMethod(_members[i]);
			}
		}

		/**
		 * Check if a member is no longer connected or has issues, if so remove it from the room, and close it's connection.
		 */
		private bool checkFaultyMember(TcpMessageChannel pMember)
		{
			try
			{
				if (!pMember.IsConnected())
				{
					Log.LogInfo("Client disconnected, removing from room", this);
					removeAndCloseMember(pMember);
					return true;
				}
				
				// Also check heartbeat timeout more aggressively
				PlayerInfo playerInfo = _server.GetPlayerInfo(pMember);
				if (playerInfo.heartbeatPending && (DateTime.Now - playerInfo.lastHeartbeatTime).TotalSeconds > 5)
				{
					Log.LogInfo("Heartbeat timeout detected in checkFaultyMember", this);
					removeAndCloseMember(pMember);
					return true;
				}
			}
			catch (Exception e)
			{
				Log.LogInfo("Exception checking connection: " + e.Message, this);
				removeAndCloseMember(pMember);
				return true;
			}
			return false;
		}

		/**
		 * Removes a member from this room and closes it's connection (basically it is being removed from the server).
		 */
		protected void removeAndCloseMember(TcpMessageChannel pMember)
		{
			// Get player info for logging
			string playerInfo = "";
			try 
			{
				var player = _server.GetPlayerInfo(pMember);
				if (player != null && !string.IsNullOrEmpty(player.name))
					playerInfo = $" ({player.name})";
			}
			catch {}
			
			// Remove from room and clean up server-side player info
			removeMember(pMember);
			_server.RemovePlayerInfo(pMember);
			
			// Close the connection
			pMember.Close();

			Log.LogInfo($"Removed client{playerInfo} at {pMember.GetRemoteEndPoint()}", this);
		}

		/**
		 * Iterate over all members and get their network messages.
		 */
		protected void receiveAndProcessNetworkMessages()
		{
			safeForEach(receiveAndProcessNetworkMessagesFromMember);
		}

		/**
		 * Get all the messages from a specific member and process them
		 */
		private void receiveAndProcessNetworkMessagesFromMember(TcpMessageChannel pMember)
		{
			while (pMember.HasMessage())
			{
				handleNetworkMessage(pMember.ReceiveMessage(), pMember);
			}
		}

		abstract protected void handleNetworkMessage(ASerializable pMessage, TcpMessageChannel pSender);

		/**
		 * Sends a message to all members in the room.
		 */
		protected void sendToAll(ASerializable pMessage)
		{
			foreach (TcpMessageChannel member in _members)
			{
				try
				{
					if (member.IsConnected())
					{
						member.SendMessage(pMessage);
					}
					else
					{
						// Member is disconnected but still in room, remove it
						removeAndCloseMember(member);
					}
				}
				catch (Exception e)
				{
					Log.LogInfo("Error sending message: " + e.Message, this);
					try { removeAndCloseMember(member); } catch {}
				}
			}
		}

		protected void sendHeartbeats()
		{
			for (int i = _members.Count - 1; i >= 0; i--)
			{
				if (i >= _members.Count) continue;
				
				TcpMessageChannel member = _members[i];
				PlayerInfo playerInfo = _server.GetPlayerInfo(member);
				
				// If no heartbeat is pending and it's been more than 3 seconds since the last one
				// (reduced from 5 seconds for more aggressive checking)
				if (!playerInfo.heartbeatPending && (DateTime.Now - playerInfo.lastHeartbeatTime).TotalSeconds > 3)
				{
					try
					{
						HeartbeatMessage heartbeat = new HeartbeatMessage();
						member.SendMessage(heartbeat);
						playerInfo.heartbeatPending = true;
					}
					catch (Exception e)
					{
						Log.LogInfo("Failed to send heartbeat: " + e.Message, this);
						removeAndCloseMember(member);
					}
				}
				
				// If a heartbeat has been pending for more than 5 seconds, the client is considered disconnected
				// (reduced from 10 seconds for more aggressive checking)
				if (playerInfo.heartbeatPending && (DateTime.Now - playerInfo.lastHeartbeatTime).TotalSeconds > 5)
				{
					Log.LogInfo("Heartbeat timeout detected in sendHeartbeats", this);
					removeAndCloseMember(member);
				}
			}
		}

		public virtual void CheckConnections()
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
							Log.LogInfo("CheckConnections found disconnected client", this);
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