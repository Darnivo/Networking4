using System;

namespace shared
{
	/**
	 * Super simple board model for TicTacToe that contains the minimal data to actually represent the board. 
	 * It doesn't say anything about whose turn it is, whether the game is finished etc.
	 * IF you want to actually implement a REAL Tic Tac Toe, that means you will have to add the data required for that (and serialize it!).
	 */
	public class TicTacToeBoardData : ASerializable
	{
		//board representation in 1d array, one element for each cell
		//0 is empty, 1 is player 1, 2 is player 2
		//might be that for your game, a 2d array is actually better
		public int[] board = new int[9] {0,0,0,0,0,0,0,0,0};

		public int currentPlayerTurn = 1;

		/**
		 * Returns who has won.
		 * 
		 * If there are any 0 on the board, noone has won yet (return 0).
		 * If there are only 1's on the board, player 1 has won (return 1).
		 * If there are only 2's on the board, player 2 has won (return 2).
		 */
		public int WhoHasWon()
		{
			// Check rows
			for (int i = 0; i < 3; i++)
			{
				if (board[i*3] != 0 && board[i*3] == board[i*3+1] && board[i*3+1] == board[i*3+2])
					return board[i*3];
			}
			
			// Check columns
			for (int i = 0; i < 3; i++)
			{
				if (board[i] != 0 && board[i] == board[i+3] && board[i+3] == board[i+6])
					return board[i];
			}
			
			// Check diagonals
			if (board[0] != 0 && board[0] == board[4] && board[4] == board[8])
				return board[0];
			
			if (board[2] != 0 && board[2] == board[4] && board[4] == board[6])
				return board[2];
			
			return 0; // No winner yet
		}
		
		public override void Serialize(Packet pPacket)
		{
			for (int i = 0; i < board.Length; i++) pPacket.Write(board[i]);
			pPacket.Write(currentPlayerTurn);
		}

		public override void Deserialize(Packet pPacket)
		{
			for (int i = 0; i < board.Length; i++) board[i] = pPacket.ReadInt();
			currentPlayerTurn = pPacket.ReadInt();
		}

		public override string ToString()
		{
			return GetType().Name +":"+ string.Join(",", board);
		}
	}
}

