﻿using System;

namespace shared
{
    /**
     * Board model used for both TicTacToe and Reaction Game.
     * For the reaction game, we just need to be able to place X and O shapes on the board.
     */
    public class TicTacToeBoardData : ASerializable
    {
        //board representation in 1d array, one element for each cell
        //0 is empty, 1 is cross (X), 2 is circle (O)
        public int[] board = new int[9] {0,0,0,0,0,0,0,0,0};

        public int currentPlayerTurn = 1;  // Not used in reaction game, but kept for compatibility

        /**
         * Returns who has won.
         * For the reaction game, this isn't used as winning is based on clicking the correct shape.
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