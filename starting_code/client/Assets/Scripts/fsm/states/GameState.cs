using shared;
using UnityEngine;
using System;

/**
 * This is where we 'play' a game.
 */
public class GameState : ApplicationStateWithView<GameView>
{
    //just for fun we keep track of how many times a player clicked the board
    //note that in the current application you have no idea whether you are player 1 or 2
    //normally it would be better to maintain this sort of info on the server if it is actually important information
    private int player1MoveCount = 0;
    private int player2MoveCount = 0;

    private string player1Name;
    private string player2Name;
    private float returnToLobbyTimer = -1;
    private bool isGameOver = false;

    public override void EnterState()
    {
        base.EnterState();
        
        // Reset game state
        player1Name = "";
        player2Name = "";
        player1MoveCount = 0;
        player2MoveCount = 0;
        isGameOver = false;
        returnToLobbyTimer = -1;
        
        // Reset UI
        view.ResetBoard();
        view.gameStatus.text = "Waiting for game to start...";
        
        // Setup event handlers
        view.gameBoard.OnCellClicked += _onCellClicked;
        view.btnConcede.onClick.AddListener(ConcedeGame);
    }

    private void _onCellClicked(int pCellIndex)
    {
        // Only send move if the game is active
        if (!isGameOver)
        {
            MakeMoveRequest makeMoveRequest = new MakeMoveRequest();
            makeMoveRequest.move = pCellIndex;
            fsm.channel.SendMessage(makeMoveRequest);
        }
    }

    private void ConcedeGame()
    {
        if (!isGameOver)
        {
            // Create a concede message
            ConcedeGameRequest concedeRequest = new ConcedeGameRequest();
            fsm.channel.SendMessage(concedeRequest);
        }
    }

    public override void ExitState()
    {
        base.ExitState();
        view.gameBoard.OnCellClicked -= _onCellClicked;
        view.btnConcede.onClick.RemoveAllListeners();
    }

    private void Update()
    {
        receiveAndProcessNetworkMessages();
        
        // Handle return to lobby timer
        if (returnToLobbyTimer > 0)
        {
            returnToLobbyTimer -= Time.deltaTime;
            // Update the text with remaining time
            int secondsLeft = Mathf.CeilToInt(returnToLobbyTimer);
            view.gameStatus.text = view.gameStatus.text.Split(',')[0] + $", returning to lobby in {secondsLeft} seconds...";
            
            if (returnToLobbyTimer <= 0)
            {
                // Timer expired, wait for server to send RoomJoinedEvent
                returnToLobbyTimer = -1;
                Debug.Log("Game over timer expired, waiting for server to move us to lobby");
            }
        }
    }

    protected override void handleNetworkMessage(ASerializable pMessage)
    {
        try {
            if (pMessage is HeartbeatMessage)
            {
                HeartbeatResponse response = new HeartbeatResponse();
                fsm.channel.SendMessage(response);
            }
            else if (pMessage is StartGameMessage)
            {
                StartGameMessage startMsg = pMessage as StartGameMessage;
                // Store player names
                player1Name = startMsg.player1Name;
                player2Name = startMsg.player2Name;
                
                // Display player names
                view.playerLabel1.text = $"Player 1: {player1Name}";
                view.playerLabel2.text = $"Player 2: {player2Name}";
                
                // Reset the board and show initial player's turn
                view.ResetBoard();
                view.gameStatus.text = $"{player1Name}'s turn";
            }
            else if (pMessage is MakeMoveResult)
            {
                handleMakeMoveResult(pMessage as MakeMoveResult);
            }
            else if (pMessage is GameOverMessage)
            {
                GameOverMessage gameOver = pMessage as GameOverMessage;
                isGameOver = true;
                
                // Display game over message and start timer
                view.gameStatus.text = gameOver.winnerName;
                returnToLobbyTimer = 5.0f; // 5 seconds before returning to lobby
            }
            else if (pMessage is PlayerDisconnectedMessage)
            {
                PlayerDisconnectedMessage disconnectMsg = pMessage as PlayerDisconnectedMessage;
                isGameOver = true;
                
                // Display disconnection message and start timer
                view.gameStatus.text = $"{disconnectMsg.playerName} left the game";
                returnToLobbyTimer = 5.0f; // 5 seconds before returning to lobby
            }
            else if (pMessage is RoomJoinedEvent)
            {
                RoomJoinedEvent roomEvent = pMessage as RoomJoinedEvent;
                if (roomEvent.room == RoomJoinedEvent.Room.LOBBY_ROOM)
                {
                    Debug.Log("Received instruction to join lobby room");
                    fsm.ChangeState<LobbyState>();
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error handling message in GameState: " + e.Message);
        }
    }

    private void handleMakeMoveResult(MakeMoveResult pMakeMoveResult)
    {
        view.gameBoard.SetBoardData(pMakeMoveResult.boardData);

        // Update the turn status
        int nextPlayer = pMakeMoveResult.boardData.currentPlayerTurn;
        string currentPlayerName = nextPlayer == 1 ? player1Name : player2Name;
        view.gameStatus.text = $"{currentPlayerName}'s turn";

        // Update move count displays
        if (pMakeMoveResult.whoMadeTheMove == 1)
        {
            player1MoveCount++;
            view.playerLabel1.text = $"Player 1: {player1Name} (Moves: {player1MoveCount})";
        }
        else if (pMakeMoveResult.whoMadeTheMove == 2)
        {
            player2MoveCount++;
            view.playerLabel2.text = $"Player 2: {player2Name} (Moves: {player2MoveCount})";
        }
    }
}
