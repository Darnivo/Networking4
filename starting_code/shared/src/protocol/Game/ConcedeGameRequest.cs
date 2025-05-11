namespace shared
{
    // Sent from CLIENT to SERVER when player wants to concede
    public class ConcedeGameRequest : ASerializable
    {
        public override void Serialize(Packet pPacket)
        { }

        public override void Deserialize(Packet pPacket)
        { }
    }
}