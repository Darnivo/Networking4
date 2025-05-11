using shared;
using System;

namespace server
{
	/**
	 * This room runs a single Game (at a time). 
	 * 
	 * The 'Game' is very simple at the moment:
	 *	- all client moves are broadcasted to all clients
	 *	
	 * The game has no end yet (that is up to you), in other words:
	 * all players that are added to this room, stay in here indefinitely.
	 */
	class GameRoom : Room
	{
		private Dictionary<TcpMessageChannel, bool> _playerReturnedToLobby = new Dictionary<TcpMessageChannel, bool>();

		public bool IsGameInPlay { get; private set; }

		//wraps the board to play on...
		private TicTacToeBoard _board = new TicTacToeBoard();

		public GameRoom(TCPGameServer pOwner) : base(pOwner)
		{
		}

		public void StartGame(TcpMessageChannel pPlayer1, TcpMessageChannel pPlayer2)
		{
			if (IsGameInPlay) throw new Exception("Game already in progress.");

			IsGameInPlay = true;
			_playerReturnedToLobby.Clear();
			
			addMember(pPlayer1);
			addMember(pPlayer2);
			
			_playerReturnedToLobby[pPlayer1] = false;
			_playerReturnedToLobby[pPlayer2] = false;

			// Initialize board with player 1's turn
			_board = new TicTacToeBoard();
			_board.GetBoardData().currentPlayerTurn = 1; // Player 1 starts

			// Send start game message with player names
			StartGameMessage startMsg = new StartGameMessage();
			startMsg.player1Name = _server.GetPlayerInfo(pPlayer1).name;
			startMsg.player2Name = _server.GetPlayerInfo(pPlayer2).name;
			sendToAll(startMsg);
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
		}

		private void handleMakeMoveRequest(MakeMoveRequest pMessage, TcpMessageChannel pSender)
		{
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
					string winnerName = _server.GetPlayerInfo(_members[winner-1]).name;
					gameOver.winnerName = $"{winnerName} wins!";
				}
				else if (isBoardFull)
				{
					gameOver.winnerName = "Game ended in a draw!";
				}
				
				// Send game over message to all players
				sendToAll(gameOver);
				
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
			if (_members.Count >= winnerIndex)
			{
				try
				{
					winnerName = _server.GetPlayerInfo(_members[winnerIndex-1]).name;
				}
				catch {}
			}
			
			// Send game over message
			GameOverMessage gameOver = new GameOverMessage();
			gameOver.winnerName = $"{winnerName} wins! {_server.GetPlayerInfo(pSender).name} conceded.";
			sendToAll(gameOver);
			
			// Schedule return to lobby
			ScheduleReturnToLobby(5000); // 5 seconds
		}

		protected override void removeMember(TcpMessageChannel pMember)
		{
			// Store player info before removing
			string playerName = "";
			try {
				PlayerInfo info = _server.GetPlayerInfo(pMember);
				if (info != null) playerName = info.name;
			} catch {}
			
			base.removeMember(pMember);
			
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
				if (_members.Count == 1)
				{
					// We need to determine who disconnected
					int remainingPlayerIndex = indexOfMember(_members[0]) + 1;
					disconnectedPlayerName = remainingPlayerIndex == 1 ? 
						_server.GetPlayerInfo(_members[0]).name : // Player 2 disconnected
						_server.GetPlayerInfo(_members[0]).name;  // Player 1 disconnected
				}
				
				// Notify remaining players
				PlayerDisconnectedMessage msg = new PlayerDisconnectedMessage();
				msg.playerName = disconnectedPlayerName;
				sendToAll(msg);
				
				// Schedule return to lobby
				ScheduleReturnToLobby(5000); // 5 seconds
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
			
			TcpMessageChannel[] players = _members.ToArray();
			
			foreach (TcpMessageChannel player in players)
			{
				try
				{
					// First check if the player is still connected
					if (player.IsConnected())
					{
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
			
			IsGameInPlay = false;
		}

		private void EndGame(string message)
		{
			IsGameInPlay = false;
			_playerReturnedToLobby.Clear();
			
			// Log game ending
			Log.LogInfo(message, this);
		}

	}
}
