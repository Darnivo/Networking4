using shared;
using UnityEngine;

/**
 * 'Chat' state while you are waiting to start a game where you can signal that you are ready or not.
 */
public class LobbyState : ApplicationStateWithView<LobbyView>
{
    [Tooltip("Should we enter the lobby in a ready state or not?")]
    [SerializeField] private bool autoQueueForGame = false;

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
        ChatMessage msg = new ChatMessage();
        msg.message = pText;
        fsm.channel.SendMessage(msg); // Now sends to server
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
        receiveAndProcessNetworkMessages();
    }
    
    protected override void handleNetworkMessage(ASerializable pMessage)
    {
        if (pMessage is ChatMessage) handleChatMessage(pMessage as ChatMessage);
        else if (pMessage is RoomJoinedEvent) handleRoomJoinedEvent(pMessage as RoomJoinedEvent);
        else if (pMessage is LobbyInfoUpdate) handleLobbyInfoUpdate(pMessage as LobbyInfoUpdate);
    }

    private void handleChatMessage(ChatMessage pMessage)
    {
        //just show the message
        AddOutput(pMessage.message);
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
        view.SetLobbyHeading($"Lobby ({pMessage.memberCount} players)");
        view.UpdatePlayerList(pMessage.playerNames);
    }

}
