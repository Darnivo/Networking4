namespace shared
{
    public class StartGameMessage : ASerializable
    {
        public string player1Name;
        public string player2Name;

        public override void Serialize(Packet pPacket)
        {
            pPacket.Write(player1Name);
            pPacket.Write(player2Name);
        }

        public override void Deserialize(Packet pPacket)
        {
            player1Name = pPacket.ReadString();
            player2Name = pPacket.ReadString();
        }
    }
}
