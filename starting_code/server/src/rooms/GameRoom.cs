using shared;
using System;

namespace server
{
	/**
	 * This room runs a single Game (at a time). 
	 * 
	 * The 'Game' is very simple at the moment:
	 *	- all client moves are broadcasted to all clients
	 */
	class GameRoom : Room
	{
		private Dictionary<TcpMessageChannel, bool> _playerReturnedToLobby = new Dictionary<TcpMessageChannel, bool>();

		public bool IsGameInPlay { get; private set; }

		//wraps the board to play on...
		private TicTacToeBoard _board = new TicTacToeBoard();
		
		// Track if players are in process of returning to lobby
		private bool _returningPlayersToLobby = false;
		
		// Last time we did an aggressive connection check
		private DateTime _lastConnectionCheck = DateTime.Now;
		
		// Tracks player names for more informative disconnect messages
		private Dictionary<TcpMessageChannel, string> _playerNames = new Dictionary<TcpMessageChannel, string>();

		private bool _gameResultBroadcast = false;

		public GameRoom(TCPGameServer pOwner) : base(pOwner)
		{
		}

		public void StartGame(TcpMessageChannel pPlayer1, TcpMessageChannel pPlayer2)
		{
			if (IsGameInPlay) throw new Exception("Game already in progress.");

			IsGameInPlay = true;
			_playerReturnedToLobby.Clear();
			_returningPlayersToLobby = false;
			_playerNames.Clear();
			_lastConnectionCheck = DateTime.Now;
			_gameResultBroadcast = false;
			
			// Additional pre-start connection check
			if (!pPlayer1.IsConnected() || !pPlayer2.IsConnected())
			{
				Log.LogInfo("One of the players disconnected before game start", this);
				if (!pPlayer1.IsConnected()) _server.GetLobbyRoom().AddMember(pPlayer2);
				if (!pPlayer2.IsConnected()) _server.GetLobbyRoom().AddMember(pPlayer1);
				IsGameInPlay = false;
				return;
			}
			
			// Store player names for better disconnect handling
			try {
				_playerNames[pPlayer1] = _server.GetPlayerInfo(pPlayer1).name;
				_playerNames[pPlayer2] = _server.GetPlayerInfo(pPlayer2).name;
			} catch {
				// Fallback if player info can't be retrieved
				_playerNames[pPlayer1] = "Player 1";
				_playerNames[pPlayer2] = "Player 2";
			}
			
			addMember(pPlayer1);
			addMember(pPlayer2);
			
			_playerReturnedToLobby[pPlayer1] = false;
			_playerReturnedToLobby[pPlayer2] = false;

			// Initialize board with player 1's turn
			_board = new TicTacToeBoard();
			_board.GetBoardData().currentPlayerTurn = 1; // Player 1 starts

			// Send start game message with player names
			StartGameMessage startMsg = new StartGameMessage();
			startMsg.player1Name = _playerNames[pPlayer1];
			startMsg.player2Name = _playerNames[pPlayer2];
			sendToAll(startMsg);
			
			// Send immediate heartbeats to both clients to establish connection
			sendImmediateHeartbeats();
		}

		protected override void addMember(TcpMessageChannel pMember)
		{
			base.addMember(pMember);

			//notify client he has joined a game room 
			RoomJoinedEvent roomJoinedEvent = new RoomJoinedEvent();
			roomJoinedEvent.room = RoomJoinedEvent.Room.GAME_ROOM;
			pMember.SendMessage(roomJoinedEvent);
		}

		public override void Update()
		{
			// Do frequent active connection checks during a game
			if (IsGameInPlay && _members.Count > 0)
			{
				// Check every 1 second while game is in play
				if ((DateTime.Now - _lastConnectionCheck).TotalSeconds >= 1)
				{
					_lastConnectionCheck = DateTime.Now;
					PerformActiveConnectionCheck();
				}
			}
			
			// Don't process more updates if we're in the process of returning players to lobby
			if (_returningPlayersToLobby) return;
			
			//check for disconnections
			int oldMemberCount = memberCount;
			bool anyDisconnected = removeFaultyMembers();
			int newMemberCount = memberCount;

			// If anyone disconnected, notify remaining players
			if (anyDisconnected || oldMemberCount != newMemberCount)
			{
				Log.LogInfo("Player(s) left the game", this);
				HandlePlayerDisconnection();
			}

			receiveAndProcessNetworkMessages();
			
			// Check if all players are back in lobby, clean up this game instance
			if (IsGameInPlay && _members.Count == 0)
			{
				IsGameInPlay = false;
				Log.LogInfo("Game ended, all players returned to lobby", this);
			}
		}
		
		private void PerformActiveConnectionCheck()
		{
			for (int i = _members.Count - 1; i >= 0; i--)
			{
				if (i >= _members.Count) continue;
				
				TcpMessageChannel member = _members[i];
				
				try
				{
					// First check if connection is still alive
					if (!member.IsConnected())
					{
						Log.LogInfo("Active check found disconnected client in game", this);
						PlayerDisconnectedFromGame(member);
						continue;
					}
					
					// Then try to send a ping with an immediate response expected
					HeartbeatMessage ping = new HeartbeatMessage();
					try
					{
						member.SendMessage(ping);
						PlayerInfo playerInfo = _server.GetPlayerInfo(member);
						playerInfo.heartbeatPending = true;
						
						// If the last heartbeat was sent more than 3 seconds ago with no response,
						// consider the client disconnected
						if ((DateTime.Now - playerInfo.lastHeartbeatTime).TotalSeconds > 3)
						{
							Log.LogInfo("Client unresponsive to heartbeat - marking as disconnected", this);
							PlayerDisconnectedFromGame(member);
						}
					}
					catch (Exception e)
					{
						Log.LogInfo("Exception sending heartbeat: " + e.Message, this);
						PlayerDisconnectedFromGame(member);
					}
				}
				catch (Exception e)
				{
					Log.LogInfo("Exception in active connection check: " + e.Message, this);
					try { removeAndCloseMember(member); } catch {}
				}
			}
		}
		
		private void PlayerDisconnectedFromGame(TcpMessageChannel member)
		{
			if (!IsGameInPlay) return;
			
			string disconnectedName = "Unknown player";
			if (_playerNames.ContainsKey(member))
			{
				disconnectedName = _playerNames[member];
			}
			
			Log.LogInfo($"Player {disconnectedName} disconnected from game", this);
			
			// Remove the player immediately
			removeAndCloseMember(member);
			
			// Notify remaining players and end the game
			HandlePlayerDisconnection();
		}
		
		private void sendImmediateHeartbeats()
		{
			foreach (TcpMessageChannel member in _members)
			{
				try
				{
					HeartbeatMessage heartbeat = new HeartbeatMessage();
					member.SendMessage(heartbeat);
					PlayerInfo playerInfo = _server.GetPlayerInfo(member);
					playerInfo.lastHeartbeatTime = DateTime.Now;
					playerInfo.heartbeatPending = true;
				}
				catch (Exception e)
				{
					Log.LogInfo("Failed to send initial heartbeat: " + e.Message, this);
				}
			}
		}

		protected override void handleNetworkMessage(ASerializable pMessage, TcpMessageChannel pSender)
		{
			if (pMessage is MakeMoveRequest)
			{
				handleMakeMoveRequest(pMessage as MakeMoveRequest, pSender);
			}
			else if (pMessage is ConcedeGameRequest)
			{
				handleConcedeGameRequest(pSender);
			}
			else if (pMessage is HeartbeatResponse)
			{
				// Update heartbeat status when a response is received
				PlayerInfo playerInfo = _server.GetPlayerInfo(pSender);
				playerInfo.lastHeartbeatTime = DateTime.Now;
				playerInfo.heartbeatPending = false;
			}
		}

		private void handleMakeMoveRequest(MakeMoveRequest pMessage, TcpMessageChannel pSender)
		{
			// Verify the game is still active
			if (!IsGameInPlay) return;
			
			// Get player ID (1 or 2)
			int playerID = indexOfMember(pSender) + 1;
			
			// Check if it's this player's turn
			if (playerID != _board.GetBoardData().currentPlayerTurn)
			{
				// Not this player's turn
				Log.LogInfo($"Not player {playerID}'s turn, ignoring move", this);
				return;
			}
			
			// Check if the move is valid (cell is empty)
			if (_board.GetBoardData().board[pMessage.move] != 0)
			{
				// Invalid move, cell already taken
				return;
			}
			
			// Make the move
			_board.MakeMove(pMessage.move, playerID);
			
			// Switch to the other player's turn
			_board.GetBoardData().currentPlayerTurn = playerID == 1 ? 2 : 1;
			
			// Send the updated board to all clients
			MakeMoveResult makeMoveResult = new MakeMoveResult();
			makeMoveResult.whoMadeTheMove = playerID;
			makeMoveResult.boardData = _board.GetBoardData();
			sendToAll(makeMoveResult);
			
			// Check win condition
			int winner = _board.GetBoardData().WhoHasWon();
			bool isBoardFull = true;
			
			// Check if board is full (draw)
			foreach (int cell in _board.GetBoardData().board)
			{
				if (cell == 0)
				{
					isBoardFull = false;
					break;
				}
			}
			
			// Handle game over conditions
			if (winner != 0 || isBoardFull)
			{
				GameOverMessage gameOver = new GameOverMessage();
				
				if (winner != 0)
				{
					int winnerIdx = winner - 1;
					string winnerName = "Unknown";
					if (winnerIdx < _members.Count)
					{
						try {
							winnerName = _playerNames[_members[winnerIdx]];
						} catch {}
					}
					gameOver.winnerName = $"{winnerName} wins!";
				}
				else if (isBoardFull)
				{
					gameOver.winnerName = "Game ended in a draw!";
				}
				
				// Send game over message to all players
				sendToAll(gameOver);
				
				if (!_gameResultBroadcast) {
					_server.BroadcastGameResultToLobby(gameOver.winnerName);
					_gameResultBroadcast = true;
				}


				// Schedule return to lobby
				ScheduleReturnToLobby(5000); // 5 seconds
			}
		}

		private void handleConcedeGameRequest(TcpMessageChannel pSender)
		{
			if (!IsGameInPlay) return;
			
			// Determine which player conceded
			int playerIndex = indexOfMember(pSender) + 1;
			int winnerIndex = playerIndex == 1 ? 2 : 1;
			
			// Find the winner
			string winnerName = "Unknown";
			string concederName = "Unknown";
			
			try {
				concederName = _playerNames[pSender];
				
				if (_members.Count >= winnerIndex && winnerIndex > 0)
				{
					winnerName = _playerNames[_members[winnerIndex-1]];
				}
			} catch {}
			
			// Send game over message
			GameOverMessage gameOver = new GameOverMessage();
			gameOver.winnerName = $"{winnerName} wins! {concederName} conceded.";
			sendToAll(gameOver);

			if (!_gameResultBroadcast) {
				_server.BroadcastGameResultToLobby(gameOver.winnerName);
				_gameResultBroadcast = true;
			}
			
			// Schedule return to lobby
			ScheduleReturnToLobby(5000); // 5 seconds
		}

		protected override void removeMember(TcpMessageChannel pMember)
		{
			// Store player info before removing
			string playerName = "";
			try {
				if (_playerNames.ContainsKey(pMember)) {
					playerName = _playerNames[pMember];
				}
			} catch {}
			
			base.removeMember(pMember);
			
			// Mark this player as returned to lobby to prevent duplicate addition
			_playerReturnedToLobby[pMember] = true;
			
			// If game is still active and a player left, notify remaining player
			if (IsGameInPlay && !string.IsNullOrEmpty(playerName))
			{
				HandlePlayerDisconnection();
			}
		}

		private void HandlePlayerDisconnection()
		{
			if (!IsGameInPlay) return;
			
			// If there are still players in the game
			if (_members.Count > 0)
			{
				// Get the disconnected player's info
				string disconnectedPlayerName = "The other player";
				
				// Find which player disconnected by comparing who's left
				if (_members.Count == 1)
				{
					TcpMessageChannel remainingPlayer = _members[0];
					
					foreach (var entry in _playerNames)
					{
						if (entry.Key != remainingPlayer)
						{
							disconnectedPlayerName = entry.Value;
							break;
						}
					}
				}
				
				// Notify remaining players
				PlayerDisconnectedMessage msg = new PlayerDisconnectedMessage();
				msg.playerName = disconnectedPlayerName;
				sendToAll(msg);
				
				if (!_gameResultBroadcast) {
					_server.BroadcastGameResultToLobby($"{disconnectedPlayerName} disconnected. Game ended.");
					_gameResultBroadcast = true;
				}

				// Schedule return to lobby immediately
				ScheduleReturnToLobby(3000); // 3 seconds (reduced from 5)
			}
			else
			{
				// No players left, end the game immediately
				EndGame("Game ended - all players disconnected");
			}
		}

		private void ScheduleReturnToLobby(int delayMs)
		{
			// Simple implementation with a timer thread
			new System.Threading.Timer((_) => {
				ReturnPlayersToLobby();
			}, null, delayMs, System.Threading.Timeout.Infinite);
		}

		private void ReturnPlayersToLobby()
		{
			if (!IsGameInPlay) return;
			
			// Set flag to prevent multiple returns and race conditions
			_returningPlayersToLobby = true;
			
			// Take a snapshot of current players
			TcpMessageChannel[] players = _members.ToArray();
			
			foreach (TcpMessageChannel player in players)
			{
				try
				{
					// Skip players already returned to lobby
					if (_playerReturnedToLobby.ContainsKey(player) && _playerReturnedToLobby[player])
					{
						continue;
					}
					
					// First check if the player is still connected
					if (player.IsConnected())
					{
						// Mark as returned to prevent duplicate addition
						_playerReturnedToLobby[player] = true;
						
						// Send the room joined event BEFORE removing from game room
						// This ensures the client receives notification before any room change
						RoomJoinedEvent roomJoinedEvent = new RoomJoinedEvent();
						roomJoinedEvent.room = RoomJoinedEvent.Room.LOBBY_ROOM;
						player.SendMessage(roomJoinedEvent);
						
						// Short delay to ensure message is sent before room transition
						System.Threading.Thread.Sleep(50);
						
						// Then transition the player
						removeMember(player);
						_server.GetLobbyRoom().AddMember(player);
						
						// Update heartbeat time to prevent immediate disconnect check
						PlayerInfo playerInfo = _server.GetPlayerInfo(player);
						playerInfo.lastHeartbeatTime = DateTime.Now;
						playerInfo.heartbeatPending = false;
					}
					else
					{
						Log.LogInfo("Player disconnected during return to lobby", this);
						removeAndCloseMember(player);
					}
				}
				catch (Exception e)
				{
					Log.LogInfo("Error returning player to lobby: " + e.Message, this);
					try
					{
						if (_members.Contains(player))
						{
							removeAndCloseMember(player);
						}
					}
					catch {}
				}
			}
			
			// Reset game state
			IsGameInPlay = false;
			_returningPlayersToLobby = false;
		}

		private void EndGame(string message)
		{
			IsGameInPlay = false;
			_playerReturnedToLobby.Clear();
			_returningPlayersToLobby = false;
			
			// Log game ending
			Log.LogInfo(message, this);
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
							Log.LogInfo("CheckConnections found disconnected client in game room", this);
							PlayerDisconnectedFromGame(member);
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