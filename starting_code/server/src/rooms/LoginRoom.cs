using shared;

namespace server
{
	/**
	 * The LoginRoom is the first room clients 'enter' until the client identifies himself with a PlayerJoinRequest. 
	 * If the client sends the wrong type of request, it will be kicked.
	 *
	 * A connected client that never sends anything will be stuck in here for life,
	 * unless the client disconnects (that will be detected in due time).
	 */ 
	class LoginRoom : SimpleRoom
	{
		//arbitrary max amount just to demo the concept
		private const int MAX_MEMBERS = 50;

		public LoginRoom(TCPGameServer pOwner) : base(pOwner)
		{
		}

		protected override void addMember(TcpMessageChannel pMember)
		{
			base.addMember(pMember);

			//notify the client that (s)he is now in the login room, clients can wait for that before doing anything else
			RoomJoinedEvent roomJoinedEvent = new RoomJoinedEvent();
			roomJoinedEvent.room = RoomJoinedEvent.Room.LOGIN_ROOM;
			pMember.SendMessage(roomJoinedEvent);
		}

		protected override void handleNetworkMessage(ASerializable pMessage, TcpMessageChannel pSender)
		{
			if (pMessage is HeartbeatResponse)
			{
				PlayerInfo playerInfo = _server.GetPlayerInfo(pSender);
				playerInfo.lastHeartbeatTime = DateTime.Now;
				playerInfo.heartbeatPending = false;
			}
			else if (pMessage is PlayerJoinRequest)
			{
				handlePlayerJoinRequest(pMessage as PlayerJoinRequest, pSender);
			}
			else //if member sends something else than a PlayerJoinRequest
			{
				Log.LogInfo("Declining client, auth request not understood", this);
				removeAndCloseMember(pSender);
			}
		}

		/**
		 * Tell the client he is accepted and move the client to the lobby room.
		 */
		private void handlePlayerJoinRequest(PlayerJoinRequest pMessage, TcpMessageChannel pSender)
		{
			// First, ensure all disconnected clients are removed
			_server.CleanupInactivePlayerInfo();
			
			// Check for duplicate name
			bool nameExists = _server.GetPlayerInfo(info => info.name == pMessage.name).Count > 0;
			
			if (nameExists)
			{
				PlayerJoinResponse response = new PlayerJoinResponse();
				response.result = PlayerJoinResponse.RequestResult.DUPLICATE_NAME;
				pSender.SendMessage(response);
				return;
			}

			// Store name and proceed
			PlayerInfo playerInfo = _server.GetPlayerInfo(pSender);
			playerInfo.name = pMessage.name;

			// player is accepted
			Log.LogInfo("Moving new client to accepted...", this);

			PlayerJoinResponse playerJoinResponse = new PlayerJoinResponse();
			playerJoinResponse.result = PlayerJoinResponse.RequestResult.ACCEPTED;
			pSender.SendMessage(playerJoinResponse);

			removeMember(pSender);
			_server.GetLobbyRoom().AddMember(pSender);
		}

	}
}
