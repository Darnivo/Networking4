using shared;
using UnityEngine;
using System;

/**
 * 'Chat' state while you are waiting to start a game where you can signal that you are ready or not.
 */
public class LobbyState : ApplicationStateWithView<LobbyView>
{
    [Tooltip("Should we enter the lobby in a ready state or not?")]
    [SerializeField] private bool autoQueueForGame = false;

    private bool hasLoggedConnectionError = false;
    private float reconnectTimer = 0f;
    private const float RECONNECT_INTERVAL = 3f;

    public override void EnterState()
    {
        base.EnterState();

        view.SetLobbyHeading("Welcome to the Lobby...");
        view.ClearOutput();
        view.AddOutput($"Server settings:"+fsm.channel.GetRemoteEndPoint());
        view.SetReadyToggle(autoQueueForGame);

        view.OnChatTextEntered += onTextEntered;
        view.OnReadyToggleClicked += onReadyToggleClicked;

        if (autoQueueForGame)
        {
            onReadyToggleClicked(true);
        }
    }

    public override void ExitState()
    {
        base.ExitState();
        
        view.OnChatTextEntered -= onTextEntered;
        view.OnReadyToggleClicked -= onReadyToggleClicked;
    }

    /**
     * Called when you enter text and press enter.
     */
    private void onTextEntered(string pText)
    {
        if (string.IsNullOrEmpty(pText)) return;
        
        ChatMessage msg = new ChatMessage();
        msg.message = pText;
        msg.sender = "";  // Initialize with empty string instead of leaving null
        
        fsm.channel.SendMessage(msg); 
        view.ClearInput();
    }

    /**
     * Called when you click on the ready checkbox
     */
    private void onReadyToggleClicked(bool pNewValue)
    {
        ChangeReadyStatusRequest msg = new ChangeReadyStatusRequest();
        msg.ready = pNewValue;
        fsm.channel.SendMessage(msg);
    }

    public void AddOutput(string pInfo)
    {
        view.AddOutput(pInfo);
    }

    /// //////////////////////////////////////////////////////////////////
    ///                     NETWORK MESSAGE PROCESSING
    /// //////////////////////////////////////////////////////////////////

    private void Update()
    {
        // Check connection status
        if (!fsm.channel.Connected)
        {
            if (!hasLoggedConnectionError)
            {
                Debug.LogWarning("Connection lost in LobbyState. Will attempt to reconnect...");
                hasLoggedConnectionError = true;
            }
            
            // Try to reconnect periodically
            reconnectTimer += Time.deltaTime;
            if (reconnectTimer >= RECONNECT_INTERVAL)
            {
                reconnectTimer = 0f;
                AttemptReconnect();
            }
            return;
        }
        
        // If we're here, we're connected
        if (hasLoggedConnectionError)
        {
            Debug.Log("Connection restored in LobbyState");
            hasLoggedConnectionError = false;
        }
        
        // Process messages as normal
        receiveAndProcessNetworkMessages();
    }

    private void AttemptReconnect()
    {
        string serverIP = "0.0.0.0"; 
        int serverPort = 55555;
        
        bool connected = fsm.channel.Connect(serverIP, serverPort);
        if (connected)
        {
            Debug.Log("Reconnected successfully");
            fsm.ChangeState<LoginState>();
        }
        else
        {
            Debug.Log("Reconnection attempt failed");
        }
    }

    
    protected override void handleNetworkMessage(ASerializable pMessage)
    {
        try
        {
            if (pMessage is HeartbeatMessage)
            {
                // Respond to heartbeats immediately
                HeartbeatResponse response = new HeartbeatResponse();
                fsm.channel.SendMessage(response);
            }
            else if (pMessage is ChatMessage) 
            {
                handleChatMessage(pMessage as ChatMessage);
            }
            else if (pMessage is RoomJoinedEvent) 
            {
                handleRoomJoinedEvent(pMessage as RoomJoinedEvent);
            }
            else if (pMessage is LobbyInfoUpdate) 
            {
                handleLobbyInfoUpdate(pMessage as LobbyInfoUpdate);
            }
            else
            {
                // Gracefully handle unexpected messages
                Debug.Log("LobbyState: Received unexpected message type: " + pMessage.GetType().Name);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error handling message in LobbyState: " + e.Message);
            // Don't let exceptions from message handling break our state
        }
    }

    private void handleChatMessage(ChatMessage pMessage)
    {
        // Format and display the chat message
        string formattedMessage = $"{pMessage.sender}: {pMessage.message}";
        view.AddOutput(formattedMessage);
    }

    private void handleRoomJoinedEvent(RoomJoinedEvent pMessage)
    {
        //did we move to the game room?
        if (pMessage.room == RoomJoinedEvent.Room.GAME_ROOM)
        {
            fsm.ChangeState<GameState>();
        }
    }

    private void handleLobbyInfoUpdate(LobbyInfoUpdate pMessage)
    {
        // Set the lobby heading with current player count and ready status
        view.SetLobbyHeading($"Lobby ({pMessage.memberCount} players, {pMessage.readyCount} ready)");
        
        // Update the player list display
        if(pMessage.playerNames != null && pMessage.playerNames.Count > 0)
        {
            view.UpdatePlayerList(pMessage.playerNames);
        }
    }

}
