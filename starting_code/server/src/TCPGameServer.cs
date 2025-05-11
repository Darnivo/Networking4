using System;
using System.Net.Sockets;
using System.Net;
using shared;
using System.Threading;
using System.Collections.Generic;
using System.Linq;

namespace server {

	/**
	 * Basic TCPGameServer that runs our game.
	 * 
	 * Server is made up out of different rooms that can hold different members.
	 * Each member is identified by a TcpMessageChannel, which can also be used for communication.
	 * In this setup each client is only member of ONE room, but you could change that of course.
	 * 
	 * Each room is responsible for cleaning up faulty clients (since it might involve gameplay, status changes etc).
	 * 
	 * As you can see this setup is limited/lacking:
	 * - only 1 game can be played at a time
	 */
	class TCPGameServer
	{

		public static void Main(string[] args)
		{
			TCPGameServer tcpGameServer = new TCPGameServer();
			tcpGameServer.run();
		}

		//we have 3 different rooms at the moment (aka simple but limited)

		private LoginRoom _loginRoom;	//this is the room every new user joins
		private LobbyRoom _lobbyRoom;	//this is the room a user moves to after a successful 'login'
		private GameRoom _gameRoom;		//this is the room a user moves to when a game is succesfully started
		private List<GameRoom> _gameRooms = new List<GameRoom>();

		//stores additional info for a player
		private Dictionary<TcpMessageChannel, PlayerInfo> _playerInfo = new Dictionary<TcpMessageChannel, PlayerInfo>();

		private TCPGameServer()
		{
			//we have only one instance of each room, this is especially limiting for the game room (since this means you can only have one game at a time).
			_loginRoom = new LoginRoom(this);
			_lobbyRoom = new LobbyRoom(this);
			_gameRoom = new GameRoom(this);
		}

		private void run()
		{
			Log.LogInfo("Starting server on port 55555", this, ConsoleColor.Gray);

			//start listening for incoming connections (with max 50 in the queue)
			TcpListener listener = new TcpListener(IPAddress.Any, 55555);
			listener.Start(50);

			int disconnectCheckCounter = 0;
			
			while (true)
			{
				//check for new members    
				if (listener.Pending())
				{
					//get the waiting client
					Log.LogInfo("Accepting new client...", this, ConsoleColor.White);
					TcpClient client = listener.AcceptTcpClient();
					//and wrap the client in an easier to use communication channel
					TcpMessageChannel channel = new TcpMessageChannel(client);
					//and add it to the login room for further 'processing'
					_loginRoom.AddMember(channel);
				}

				// Periodically check for disconnected clients
				disconnectCheckCounter++;
				if (disconnectCheckCounter >= 10) // Check more frequently (reduced from 20 to 10)
				{
					disconnectCheckCounter = 0;
					
					// Force all rooms to check connections
					_loginRoom.CheckConnections();
					_lobbyRoom.CheckConnections();
					_gameRoom.CheckConnections();
					
					foreach (GameRoom game in _gameRooms.ToList())
					{
						game.CheckConnections();
					}
					
					// Also clean up inactive player info to prevent stale data
					CleanupInactivePlayerInfo();
				}

				//now update every single room
				_loginRoom.Update();
				_lobbyRoom.Update();
				_gameRoom.Update();

				// Clean up completed game rooms
				CleanupEmptyGameRooms();

				foreach (GameRoom game in _gameRooms.ToList())
				{
					game.Update();
				}

				Thread.Sleep(50);
			}
		}

		//provide access to the different rooms on the server 
		public LoginRoom GetLoginRoom() { return _loginRoom; }
		public LobbyRoom GetLobbyRoom() { return _lobbyRoom; }
		public GameRoom GetGameRoom() { return _gameRoom; }

		/**
		 * Returns a handle to the player info for the given client 
		 * (will create new player info if there was no info for the given client yet)
		 */
		public PlayerInfo GetPlayerInfo (TcpMessageChannel pClient)
		{
			if (!_playerInfo.ContainsKey(pClient))
			{
				_playerInfo[pClient] = new PlayerInfo();
			}

			return _playerInfo[pClient];
		}

		/**
		 * Returns a list of all players that match the predicate, e.g. to get a list of 
		 * all players named bob, you would do:
		 *	GetPlayerInfo((playerInfo) => playerInfo.name == "bob");
		 */
		public List<PlayerInfo> GetPlayerInfo(Predicate<PlayerInfo> pPredicate)
		{
			return _playerInfo.Values.ToList<PlayerInfo>().FindAll(pPredicate);
		}

		/**
		 * Should be called by a room when a member is closed and removed.
		 */
		public void RemovePlayerInfo (TcpMessageChannel pClient)
		{
			_playerInfo.Remove(pClient);
		}

		public GameRoom CreateNewGame()
		{
			GameRoom newGame = new GameRoom(this);
			_gameRooms.Add(newGame);
			return newGame;
		}

		public void CleanupInactivePlayerInfo()
		{
			List<TcpMessageChannel> toRemove = new List<TcpMessageChannel>();
			
			foreach (var client in _playerInfo.Keys)
			{
				try
				{
					if (!client.IsConnected())
					{
						toRemove.Add(client);
					}
				}
				catch
				{
					toRemove.Add(client);
				}
			}
			
			foreach (var client in toRemove)
			{
				// Log removal only if player had a name
				var playerInfo = _playerInfo[client];
				if (!string.IsNullOrEmpty(playerInfo.name))
				{
					Log.LogInfo("Removing inactive player info for " + playerInfo.name, this);
				}
				_playerInfo.Remove(client);
			}
		}
		
		private void CleanupEmptyGameRooms()
		{
			List<GameRoom> roomsToRemove = new List<GameRoom>();
			
			foreach (GameRoom game in _gameRooms)
			{
				// If game is not in play and has no members, it can be removed
				if (!game.IsGameInPlay && game.memberCount == 0)
				{
					roomsToRemove.Add(game);
				}
			}
			
			foreach (GameRoom game in roomsToRemove)
			{
				_gameRooms.Remove(game);
				Log.LogInfo("Removed completed game room", this);
			}
		}

		public void BroadcastGameResultToLobby(string result)
		{
			_lobbyRoom.BroadcastGameResult(result);
		}
	}
}