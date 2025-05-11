namespace shared
{
    // Sent from SERVER to CLIENT when the other player disconnects
    public class PlayerDisconnectedMessage : ASerializable
    {
        public string playerName;

        public override void Serialize(Packet pPacket)
        {
            pPacket.Write(playerName);
        }

        public override void Deserialize(Packet pPacket)
        {
            playerName = pPacket.ReadString();
        }
    }
}