namespace shared
{
	/**
	 * BIDIRECTIONAL Chat message for the lobby
	 */
	public class ChatMessage : ASerializable
	{
		public string sender;
		public string message;

		public override void Serialize(Packet pPacket)
		{
			pPacket.Write(sender);
			pPacket.Write(message);
		}

		public override void Deserialize(Packet pPacket)
		{
			sender = pPacket.ReadString();
			message = pPacket.ReadString();
		}

	}
}
