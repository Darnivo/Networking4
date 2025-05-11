namespace shared
{
    // Sent from SERVER > CLIENT to check if the client is still alive
    public class HeartbeatMessage : ASerializable
    {
        public override void Serialize(Packet pPacket)
        { }

        public override void Deserialize(Packet pPacket)
        { }
    }
}