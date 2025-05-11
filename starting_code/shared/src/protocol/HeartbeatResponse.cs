namespace shared
{
    // Sent from CLIENT > SERVER in response to a HeartbeatMessage
    public class HeartbeatResponse : ASerializable
    {
        public override void Serialize(Packet pPacket)
        { }

        public override void Deserialize(Packet pPacket)
        { }
    }
}