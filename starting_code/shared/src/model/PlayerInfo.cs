using System;

namespace shared
{
    /**
     * Empty placeholder class for the PlayerInfo object which is being tracked for each client by the server.
     * Add any data you want to store for the player here and make it extend ASerializable.
     */
    public class PlayerInfo 
    {
        public string name { get; set; }
        public DateTime lastHeartbeatTime { get; set; } = DateTime.Now;
        public bool heartbeatPending { get; set; } = false;
    }
}
