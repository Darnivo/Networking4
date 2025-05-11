using shared;
using UnityEngine;
using System;
using System.Collections;

/**
 * Game state for the reaction-based shape clicking game.
 */
public class GameState : ApplicationStateWithView<GameView>
{
    private string player1Name;
    private string player2Name;
    private int player1Score = 0;
    private int player2Score = 0;
    private float returnToLobbyTimer = -1;
    private bool isGameOver = false;

    // Reaction game specific fields
    private int currentRound = 0;
    private int crossPosition = -1;
    private int circlePosition = -1;
    private int targetShape = -1; // 1=Cross, 2=Circle
    private float countdownTimer = 0;
    private bool roundActive = false;
    private bool boardCleared = true;

    public override void EnterState()
    {
        base.EnterState();
        
        // Reset game state
        player1Name = "";
        player2Name = "";
        player1Score = 0;
        player2Score = 0;
        isGameOver = false;
        returnToLobbyTimer = -1;
        
        currentRound = 0;
        crossPosition = -1;
        circlePosition = -1;
        targetShape = -1;
        countdownTimer = 0;
        roundActive = false;
        boardCleared = true;
        
        // Reset UI
        view.ResetBoard();
        view.gameStatus.text = "Waiting for game to start...";
        
        // Setup event handlers
        view.gameBoard.OnCellClicked += OnCellClicked;
        view.btnConcede.onClick.AddListener(ConcedeGame);
    }

    private void OnCellClicked(int pCellIndex)
    {
        // Only process clicks if a round is active and game is not over
        if (roundActive && !isGameOver)
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
            ConcedeGameRequest concedeRequest = new ConcedeGameRequest();
            fsm.channel.SendMessage(concedeRequest);
        }
    }

    public override void ExitState()
    {
        base.ExitState();
        view.gameBoard.OnCellClicked -= OnCellClicked;
        view.btnConcede.onClick.RemoveAllListeners();
    }

    private void Update()
    {
        receiveAndProcessNetworkMessages();
        
        // Only process game timers if the game isn't over
        if (!isGameOver)
        {
            // Handle countdown timer for round
            if (countdownTimer > 0)
            {
                countdownTimer -= Time.deltaTime;
                int secondsRemaining = Mathf.CeilToInt(countdownTimer);
                
                // Update countdown text
                view.gameStatus.text = $"Round {currentRound}/3 - Starts in {secondsRemaining}...";
                
                // When countdown reaches zero, prepare board
                if (countdownTimer <= 0 && !roundActive && boardCleared)
                {
                    PrepareRoundBoard();
                }
            }
        }
        
        // Handle return to lobby timer - this should work even if game is over
        if (returnToLobbyTimer > 0)
        {
            returnToLobbyTimer -= Time.deltaTime;
            // Update the text with remaining time
            int secondsLeft = Mathf.CeilToInt(returnToLobbyTimer);
            
            // Make sure we don't lose the original message
            string baseMessage = view.gameStatus.text;
            if (baseMessage.Contains(", returning to lobby in"))
            {
                baseMessage = baseMessage.Substring(0, baseMessage.IndexOf(", returning to lobby in"));
            }
            
            view.gameStatus.text = baseMessage + $", returning to lobby in {secondsLeft} seconds...";
            
            if (returnToLobbyTimer <= 0)
            {
                // Timer expired, wait for server to send RoomJoinedEvent
                returnToLobbyTimer = -1;
                Debug.Log("Game over timer expired, waiting for server to move us to lobby");
            }
        }
    }

    private void PrepareRoundBoard()
    {
        // Don't update anything if the game is over
        if (isGameOver) return;
        
        // Clear the board visually first
        ClearBoard();
        
        // Create shapes on the board
        if (crossPosition >= 0)
        {
            TicTacToeBoardData boardData = new TicTacToeBoardData();
            boardData.board[crossPosition] = 1; // Cross
            if (circlePosition >= 0) boardData.board[circlePosition] = 2; // Circle
            view.gameBoard.SetBoardData(boardData);
        }
        
        // Set instruction text
        string targetText = targetShape == 1 ? "Click the cross (X)!" : "Click the circle (O)!";
        view.gameStatus.text = targetText;
        
        // Activate round
        roundActive = true;
        boardCleared = false;
    }
    
    private void ClearBoard()
    {
        // Reset board to empty
        TicTacToeBoardData emptyBoard = new TicTacToeBoardData();
        view.gameBoard.SetBoardData(emptyBoard);
        boardCleared = true;
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
                HandleStartGameMessage(pMessage as StartGameMessage);
            }
            else if (pMessage is RoundStartMessage)
            {
                HandleRoundStartMessage(pMessage as RoundStartMessage);
            }
            else if (pMessage is RoundResultMessage)
            {
                HandleRoundResultMessage(pMessage as RoundResultMessage);
            }
            else if (pMessage is GameOverMessage)
            {
                HandleGameOverMessage(pMessage as GameOverMessage);
            }
            else if (pMessage is PlayerDisconnectedMessage)
            {
                HandlePlayerDisconnectedMessage(pMessage as PlayerDisconnectedMessage);
            }
            else if (pMessage is RoomJoinedEvent)
            {
                HandleRoomJoinedEvent(pMessage as RoomJoinedEvent);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error handling message in GameState: " + e.Message);
        }
    }

    private void HandleStartGameMessage(StartGameMessage startMsg)
    {
        // Store player names
        player1Name = startMsg.player1Name;
        player2Name = startMsg.player2Name;
        
        // Display player names
        view.playerLabel1.text = $"Player 1: {player1Name}";
        view.playerLabel2.text = $"Player 2: {player2Name}";
        
        // Reset the board and show waiting message
        view.ResetBoard();
        view.gameStatus.text = "Game starting soon...";
        
        // Reset scores
        player1Score = 0;
        player2Score = 0;
    }

    private void HandleRoundStartMessage(RoundStartMessage roundMsg)
    {
        // Store round information
        currentRound = roundMsg.roundNumber;
        crossPosition = roundMsg.crossPosition[0];
        circlePosition = roundMsg.circlePosition[0];
        targetShape = roundMsg.targetShape;
        
        // Start countdown
        countdownTimer = roundMsg.countdownSeconds;
        roundActive = false;
        
        // Clear board for countdown
        ClearBoard();
        
        // Update score display
        UpdateScoreDisplay();
    }

    private void HandleRoundResultMessage(RoundResultMessage resultMsg)
    {
        // Check if game is already over (from disconnection or concede)
        if (isGameOver) return;
    
        // Update scores
        player1Score = resultMsg.player1Score;
        player2Score = resultMsg.player2Score;
        
        // Stop active round
        roundActive = false;
        
        // Clear the board
        ClearBoard();
        
        // Show round result
        string winnerName = resultMsg.winnerPlayerID == 1 ? player1Name : player2Name;
        if (resultMsg.winnerPlayerID > 0) {
            view.gameStatus.text = $"{winnerName} clicked it faster!\n(Scoreline: {player1Name} {player1Score} - {player2Score} {player2Name})";
        } else {
            view.gameStatus.text = $"No one clicked in time!\n(Scoreline: {player1Name} {player1Score} - {player2Score} {player2Name})";
        }
        
        // Update score display
        UpdateScoreDisplay();
        
        // If game is over, show game over message
        if (resultMsg.isGameOver)
        {
            isGameOver = true;
            
            // Check if it's a tie
            if (resultMsg.gameWinner == 0 || player1Score == player2Score) {
                view.gameStatus.text = $"Game ended in a tie {player1Score} - {player2Score}";
            } else {
                string gameWinnerName = resultMsg.gameWinner == 1 ? player1Name : player2Name;
                view.gameStatus.text = $"{gameWinnerName} won the game {player1Score} - {player2Score}";
            }
        }
    }
    
    private void UpdateScoreDisplay()
    {
        view.playerLabel1.text = $"Player 1: {player1Name} (Score: {player1Score})";
        view.playerLabel2.text = $"Player 2: {player2Name} (Score: {player2Score})";
    }

    private void HandleGameOverMessage(GameOverMessage gameOver)
    {
        isGameOver = true;
        
        // Display game over message and start timer
        // Check if the message indicates a tie
        if (gameOver.winnerName.Contains("tie")) {
            view.gameStatus.text = gameOver.winnerName;
        } else {
            view.gameStatus.text = gameOver.winnerName;
        }
        
        returnToLobbyTimer = 5.0f; // 5 seconds before returning to lobby
    }

    private void HandlePlayerDisconnectedMessage(PlayerDisconnectedMessage disconnectMsg)
    {
        isGameOver = true;
        
        // Display disconnection message and start timer
        view.gameStatus.text = $"{disconnectMsg.playerName} left the game";
        returnToLobbyTimer = 5.0f; // 5 seconds before returning to lobby
    }

    private void HandleRoomJoinedEvent(RoomJoinedEvent roomEvent)
    {
        if (roomEvent.room == RoomJoinedEvent.Room.LOBBY_ROOM)
        {
            Debug.Log("Received instruction to join lobby room");
            fsm.ChangeState<LobbyState>();
        }
    }
}