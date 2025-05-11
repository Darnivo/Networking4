namespace shared
{
    // Sent from SERVER to CLIENTs at the start of each round
    public class RoundStartMessage : ASerializable
    {
        public int roundNumber;    // Current round (1-3)
        public int[] crossPosition; // Position of the cross (X) on the board
        public int[] circlePosition; // Position of the circle (O) on the board
        public int targetShape;     // 1 for cross, 2 for circle
        public int countdownSeconds; // Countdown before round starts

        public override void Serialize(Packet pPacket)
        {
            pPacket.Write(roundNumber);
            pPacket.Write(crossPosition[0]); 
            pPacket.Write(circlePosition[0]); 
            pPacket.Write(targetShape);
            pPacket.Write(countdownSeconds);
        }

        public override void Deserialize(Packet pPacket)
        {
            roundNumber = pPacket.ReadInt();
            crossPosition = new int[1];
            circlePosition = new int[1];
            crossPosition[0] = pPacket.ReadInt();
            circlePosition[0] = pPacket.ReadInt();
            targetShape = pPacket.ReadInt();
            countdownSeconds = pPacket.ReadInt();
        }
    }

    // Sent from SERVER to CLIENTs after a player successfully clicks the correct shape
    public class RoundResultMessage : ASerializable
    {
        public int roundNumber;      // Current round (1-3)
        public int winnerPlayerID;   // Player ID who won the round (1 or 2)
        public int player1Score;     // Current score for player 1
        public int player2Score;     // Current score for player 2
        public bool isGameOver;      // True if this was the final round
        public int gameWinner;       // Final winner (1 or 2), only valid if isGameOver is true

        public override void Serialize(Packet pPacket)
        {
            pPacket.Write(roundNumber);
            pPacket.Write(winnerPlayerID);
            pPacket.Write(player1Score);
            pPacket.Write(player2Score);
            pPacket.Write(isGameOver);
            pPacket.Write(gameWinner);
        }

        public override void Deserialize(Packet pPacket)
        {
            roundNumber = pPacket.ReadInt();
            winnerPlayerID = pPacket.ReadInt();
            player1Score = pPacket.ReadInt();
            player2Score = pPacket.ReadInt();
            isGameOver = pPacket.ReadBool();
            gameWinner = pPacket.ReadInt();
        }
    }
}