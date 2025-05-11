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
			addMember(pPlayer1);
			addMember(pPlayer2);

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
			//demo of how we can tell people have left the game...
			int oldMemberCount = memberCount;
			base.Update();
			int newMemberCount = memberCount;

			if (oldMemberCount != newMemberCount)
			{
				Log.LogInfo("People left the game...", this);
			}
		}

		protected override void handleNetworkMessage(ASerializable pMessage, TcpMessageChannel pSender)
		{
			if (pMessage is HeartbeatResponse)
			{
				PlayerInfo playerInfo = _server.GetPlayerInfo(pSender);
				playerInfo.lastHeartbeatTime = DateTime.Now;
				playerInfo.heartbeatPending = false;
			}
			else if (pMessage is MakeMoveRequest)
			{
				handleMakeMoveRequest(pMessage as MakeMoveRequest, pSender);
			}
		}

		// In GameRoom.cs - handleMakeMoveRequest method
		private void handleMakeMoveRequest(MakeMoveRequest pMessage, TcpMessageChannel pSender)
		{
			// Get player ID (1 or 2)
			int playerID = indexOfMember(pSender) + 1;
			
			// Check if the move is valid (cell is empty)
			if (_board.GetBoardData().board[pMessage.move] != 0)
			{
				// Invalid move, cell already taken
				return;
			}
			
			// Make the move
			_board.MakeMove(pMessage.move, playerID);
			
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
					gameOver.winnerName = winnerName;
				}
				else if (isBoardFull)
				{
					gameOver.winnerName = "Draw - No Winner";
				}
				
				// Send game over message to all players
				sendToAll(gameOver);
				
				// Send all players back to lobby
				TcpMessageChannel[] playersToReturn = _members.ToArray();
				foreach (TcpMessageChannel player in playersToReturn)
				{
					// Create notification
					ChatMessage notification = new ChatMessage();
					notification.sender = "[Server]";
					notification.message = $"Game over! {gameOver.winnerName}";
					
					// Remove from game room
					removeMember(player);
					
					// Add to lobby
					_server.GetLobbyRoom().AddMember(player);
					
					// Send notification to the player
					player.SendMessage(notification);
				}
				
				// Reset game state
				IsGameInPlay = false;
			}
		}
	}
}
