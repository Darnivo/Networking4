using shared;
using System;
using System.Collections.Generic;
using System.Threading;

namespace server
{
    /**
     * This room runs a single Game (at a time). 
     * Implements a reaction-based game where players race to click the correct shape.
     */
    class GameRoom : Room
    {
        private Dictionary<TcpMessageChannel, bool> _playerReturnedToLobby = new Dictionary<TcpMessageChannel, bool>();

        public bool IsGameInPlay { get; private set; }

        // Game state for reaction game
        private int _currentRound = 0;
        private int _player1Score = 0;
        private int _player2Score = 0;
        private int _crossPosition = -1;
        private int _circlePosition = -1;
        private int _targetShape = -1; // 1 for cross, 2 for circle
        private bool _awaitingClicks = false;
        private Random _random = new Random();
        
        // Track if players are in process of returning to lobby
        private bool _returningPlayersToLobby = false;
        
        // Last time we did an aggressive connection check
        private DateTime _lastConnectionCheck = DateTime.Now;
        
        // Tracks player names for more informative disconnect messages
        private Dictionary<TcpMessageChannel, string> _playerNames = new Dictionary<TcpMessageChannel, string>();

        private bool _gameResultBroadcast = false;
        
        // Timer for managing round countdowns
        private Timer _roundTimer = null;

        public GameRoom(TCPGameServer pOwner) : base(pOwner)
        {
        }

        public void StartGame(TcpMessageChannel pPlayer1, TcpMessageChannel pPlayer2)
        {
            if (IsGameInPlay) throw new Exception("Game already in progress.");

            IsGameInPlay = true;
            _playerReturnedToLobby.Clear();
            _returningPlayersToLobby = false;
            _playerNames.Clear();
            _lastConnectionCheck = DateTime.Now;
            _gameResultBroadcast = false;
            
            // Reset game state
            _currentRound = 0;
            _player1Score = 0;
            _player2Score = 0;
            _awaitingClicks = false;
            
            // Additional pre-start connection check
            if (!pPlayer1.IsConnected() || !pPlayer2.IsConnected())
            {
                Log.LogInfo("One of the players disconnected before game start", this);
                if (!pPlayer1.IsConnected()) _server.GetLobbyRoom().AddMember(pPlayer2);
                if (!pPlayer2.IsConnected()) _server.GetLobbyRoom().AddMember(pPlayer1);
                IsGameInPlay = false;
                return;
            }
            
            // Store player names for better disconnect handling
            try {
                _playerNames[pPlayer1] = _server.GetPlayerInfo(pPlayer1).name;
                _playerNames[pPlayer2] = _server.GetPlayerInfo(pPlayer2).name;
            } catch {
                // Fallback if player info can't be retrieved
                _playerNames[pPlayer1] = "Player 1";
                _playerNames[pPlayer2] = "Player 2";
            }
            
            addMember(pPlayer1);
            addMember(pPlayer2);
            
            _playerReturnedToLobby[pPlayer1] = false;
            _playerReturnedToLobby[pPlayer2] = false;

            // Send start game message with player names
            StartGameMessage startMsg = new StartGameMessage();
            startMsg.player1Name = _playerNames[pPlayer1];
            startMsg.player2Name = _playerNames[pPlayer2];
            sendToAll(startMsg);
            
            // Send immediate heartbeats to both clients to establish connection
            sendImmediateHeartbeats();
            
            // Start the first round after a short delay
            _roundTimer = new Timer(_ => StartNextRound(), null, 2000, Timeout.Infinite);
        }

        protected override void addMember(TcpMessageChannel pMember)
        {
            base.addMember(pMember);

            // Notify client they have joined a game room 
            RoomJoinedEvent roomJoinedEvent = new RoomJoinedEvent();
            roomJoinedEvent.room = RoomJoinedEvent.Room.GAME_ROOM;
            pMember.SendMessage(roomJoinedEvent);
        }

        private void StartNextRound()
        {
            _currentRound++;
            
            // Check if we've completed 3 rounds or if one player has already won 2 rounds
            if (_currentRound > 3 || _player1Score >= 2 || _player2Score >= 2)
            {
                EndGame();
                return;
            }
            
            // Check for a potential tie after round 3
            if (_currentRound == 3 && _player1Score == _player2Score)
            {
                // If we're entering round 3 and scores are tied at 1-1, this will be the tiebreaker
                // Continue with round 3
            }
            
            // Generate positions for cross and circle - ensure they're not the same
            _crossPosition = _random.Next(0, 9);
            do {
                _circlePosition = _random.Next(0, 9);
            } while (_circlePosition == _crossPosition);
            
            // Randomly select target shape (1=cross, 2=circle)
            _targetShape = _random.Next(1, 3);
            
            // Create the round start message
            RoundStartMessage roundMsg = new RoundStartMessage();
            roundMsg.roundNumber = _currentRound;
            roundMsg.crossPosition = new int[1] { _crossPosition };
            roundMsg.circlePosition = new int[1] { _circlePosition };
            roundMsg.targetShape = _targetShape;
            roundMsg.countdownSeconds = 3;
            
            // Send to all players
            sendToAll(roundMsg);
            
            // After countdown, enable clicking
            _roundTimer = new Timer(_ => {
                _awaitingClicks = true;
                // If neither player clicks after 10 seconds, move to next round
                _roundTimer = new Timer(_ => {
                    if (_awaitingClicks) {
                        // Nobody clicked in time, move to next round with no winner
                        RoundResultMessage resultMsg = new RoundResultMessage();
                        resultMsg.roundNumber = _currentRound;
                        resultMsg.winnerPlayerID = 0; // No winner
                        resultMsg.player1Score = _player1Score;
                        resultMsg.player2Score = _player2Score;
                        
                        // Check if this is the last round and determines game outcome
                        bool isGameOver = (_currentRound >= 3 || _player1Score >= 2 || _player2Score >= 2);
                        int gameWinner = (_player1Score > _player2Score) ? 1 : 
                                         (_player2Score > _player1Score) ? 2 : 0; // 0 means tie
                        
                        resultMsg.isGameOver = isGameOver;
                        resultMsg.gameWinner = gameWinner;
                        
                        sendToAll(resultMsg);
                        
                        if (isGameOver) {
                            EndGame(); // End the game if this was the last round
                        } else {
                            // Start next round after a delay
                            _roundTimer = new Timer(_ => StartNextRound(), null, 3000, Timeout.Infinite);
                        }
                    }
                }, null, 10000, Timeout.Infinite);
            }, null, 3000, Timeout.Infinite);
        }
        
        private void EndGame()
        {
            // Cancel any pending timers
            if (_roundTimer != null)
            {
                _roundTimer.Dispose();
                _roundTimer = null;
            }
            
            // Determine winner or tie
            string resultMessage;
            
            if (_player1Score == _player2Score) {
                // It's a tie
                resultMessage = "Game ended in a tie " + _player1Score + "-" + _player2Score;
            } else {
                // There's a winner
                int winner = (_player1Score > _player2Score) ? 1 : 2;
                string winnerName = (winner == 1) ? 
                    (_members.Count > 0 ? _playerNames[_members[0]] : "Player 1") : 
                    (_members.Count > 1 ? _playerNames[_members[1]] : "Player 2");
                
                resultMessage = winnerName + " won the game " + _player1Score + "-" + _player2Score;
            }
            
            // Create game over message
            GameOverMessage gameOver = new GameOverMessage();
            gameOver.winnerName = resultMessage;
            sendToAll(gameOver);
            
            // Broadcast result to lobby
            if (!_gameResultBroadcast) {
                _server.BroadcastGameResultToLobby(gameOver.winnerName);
                _gameResultBroadcast = true;
            }
            
            // Mark game as no longer awaiting clicks
            _awaitingClicks = false;
            
            // Schedule return to lobby
            ScheduleReturnToLobby(5000);
        }

        public override void Update()
        {
            // Do frequent active connection checks during a game
            if (IsGameInPlay && _members.Count > 0)
            {
                // Check every 1 second while game is in play
                if ((DateTime.Now - _lastConnectionCheck).TotalSeconds >= 1)
                {
                    _lastConnectionCheck = DateTime.Now;
                    PerformActiveConnectionCheck();
                }
            }
            
            // Don't process more updates if we're in the process of returning players to lobby
            if (_returningPlayersToLobby) return;
            
            //check for disconnections
            int oldMemberCount = memberCount;
            bool anyDisconnected = removeFaultyMembers();
            int newMemberCount = memberCount;

            // If anyone disconnected, notify remaining players
            if (anyDisconnected || oldMemberCount != newMemberCount)
            {
                Log.LogInfo("Player(s) left the game", this);
                HandlePlayerDisconnection();
            }

            receiveAndProcessNetworkMessages();
            
            // Check if all players are back in lobby, clean up this game instance
            if (IsGameInPlay && _members.Count == 0)
            {
                IsGameInPlay = false;
                Log.LogInfo("Game ended, all players returned to lobby", this);
            }
        }
        
        private void PerformActiveConnectionCheck()
        {
            for (int i = _members.Count - 1; i >= 0; i--)
            {
                if (i >= _members.Count) continue;
                
                TcpMessageChannel member = _members[i];
                
                try
                {
                    // First check if connection is still alive
                    if (!member.IsConnected())
                    {
                        Log.LogInfo("Active check found disconnected client in game", this);
                        PlayerDisconnectedFromGame(member);
                        continue;
                    }
                    
                    // Then try to send a ping with an immediate response expected
                    HeartbeatMessage ping = new HeartbeatMessage();
                    try
                    {
                        member.SendMessage(ping);
                        PlayerInfo playerInfo = _server.GetPlayerInfo(member);
                        playerInfo.heartbeatPending = true;
                        
                        // If the last heartbeat was sent more than 3 seconds ago with no response,
                        // consider the client disconnected
                        if ((DateTime.Now - playerInfo.lastHeartbeatTime).TotalSeconds > 3)
                        {
                            Log.LogInfo("Client unresponsive to heartbeat - marking as disconnected", this);
                            PlayerDisconnectedFromGame(member);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.LogInfo("Exception sending heartbeat: " + e.Message, this);
                        PlayerDisconnectedFromGame(member);
                    }
                }
                catch (Exception e)
                {
                    Log.LogInfo("Exception in active connection check: " + e.Message, this);
                    try { removeAndCloseMember(member); } catch {}
                }
            }
        }
        
        private void PlayerDisconnectedFromGame(TcpMessageChannel member)
        {
            if (!IsGameInPlay) return;
            
            string disconnectedName = "Unknown player";
            if (_playerNames.ContainsKey(member))
            {
                disconnectedName = _playerNames[member];
            }
            
            Log.LogInfo($"Player {disconnectedName} disconnected from game", this);
            
            // Remove the player immediately
            removeAndCloseMember(member);
            
            // Notify remaining players and end the game
            HandlePlayerDisconnection();
        }
        
        private void sendImmediateHeartbeats()
        {
            foreach (TcpMessageChannel member in _members)
            {
                try
                {
                    HeartbeatMessage heartbeat = new HeartbeatMessage();
                    member.SendMessage(heartbeat);
                    PlayerInfo playerInfo = _server.GetPlayerInfo(member);
                    playerInfo.lastHeartbeatTime = DateTime.Now;
                    playerInfo.heartbeatPending = true;
                }
                catch (Exception e)
                {
                    Log.LogInfo("Failed to send initial heartbeat: " + e.Message, this);
                }
            }
        }

        protected override void handleNetworkMessage(ASerializable pMessage, TcpMessageChannel pSender)
        {
            if (pMessage is MakeMoveRequest)
            {
                handleMakeMoveRequest(pMessage as MakeMoveRequest, pSender);
            }
            else if (pMessage is ConcedeGameRequest)
            {
                handleConcedeGameRequest(pSender);
            }
            else if (pMessage is HeartbeatResponse)
            {
                // Update heartbeat status when a response is received
                PlayerInfo playerInfo = _server.GetPlayerInfo(pSender);
                playerInfo.lastHeartbeatTime = DateTime.Now;
                playerInfo.heartbeatPending = false;
            }
        }

        private void handleMakeMoveRequest(MakeMoveRequest pMessage, TcpMessageChannel pSender)
        {
            // Verify the game is still active and we're awaiting clicks
            if (!IsGameInPlay || !_awaitingClicks) return;
            
            int playerID = indexOfMember(pSender) + 1; // 1 or 2
            int clickedPosition = pMessage.move;
            
            // Check if click was on the correct target
            bool isCorrect = false;
            
            if (_targetShape == 1 && clickedPosition == _crossPosition) {
                // Target was Cross and player clicked Cross
                isCorrect = true;
            } else if (_targetShape == 2 && clickedPosition == _circlePosition) {
                // Target was Circle and player clicked Circle
                isCorrect = true;
            }
            
            if (isCorrect) {
                // Player clicked correct position
                _awaitingClicks = false; // Stop accepting clicks
                
                // Update scores
                if (playerID == 1) {
                    _player1Score++;
                } else {
                    _player2Score++;
                }
                
                // Check if game is over (player reached 2 wins or we've done 3 rounds)
                bool isGameOver = (_player1Score >= 2 || _player2Score >= 2 || _currentRound >= 3);
                
                // Determine winner or tie
                int gameWinner = 0; // 0 means tie
                if (_player1Score > _player2Score) {
                    gameWinner = 1;
                } else if (_player2Score > _player1Score) {
                    gameWinner = 2;
                }
                
                // Create round result message
                RoundResultMessage resultMsg = new RoundResultMessage();
                resultMsg.roundNumber = _currentRound;
                resultMsg.winnerPlayerID = playerID;
                resultMsg.player1Score = _player1Score;
                resultMsg.player2Score = _player2Score;
                resultMsg.isGameOver = isGameOver;
                resultMsg.gameWinner = gameWinner;
                
                sendToAll(resultMsg);
                
                // If game over, end game, otherwise start next round
                if (isGameOver) {
                    EndGame();
                } else {
                    // Start next round after a delay
                    _roundTimer = new Timer(_ => StartNextRound(), null, 3000, Timeout.Infinite);
                }
            }
            // If not correct, do nothing - let player keep trying
        }

        private void handleConcedeGameRequest(TcpMessageChannel pSender)
        {
            if (!IsGameInPlay) return;
            
            // Cancel any pending timers
            if (_roundTimer != null)
            {
                _roundTimer.Dispose();
                _roundTimer = null;
            }
            
            // Mark game as no longer awaiting clicks
            _awaitingClicks = false;
            
            // Determine which player conceded
            int playerIndex = indexOfMember(pSender) + 1;
            int winnerIndex = playerIndex == 1 ? 2 : 1;
            
            // Find the winner
            string winnerName = "Unknown";
            string concederName = "Unknown";
            
            try {
                concederName = _playerNames[pSender];
                
                if (_members.Count >= winnerIndex && winnerIndex > 0)
                {
                    winnerName = _playerNames[_members[winnerIndex-1]];
                }
            } catch {}
            
            // Send game over message
            GameOverMessage gameOver = new GameOverMessage();
            gameOver.winnerName = $"{winnerName} wins! {concederName} conceded.";
            sendToAll(gameOver);

            if (!_gameResultBroadcast) {
                _server.BroadcastGameResultToLobby(gameOver.winnerName);
                _gameResultBroadcast = true;
            }
            
            // Schedule return to lobby
            ScheduleReturnToLobby(5000); // 5 seconds
        }

        protected override void removeMember(TcpMessageChannel pMember)
        {
            // Store player info before removing
            string playerName = "";
            try {
                if (_playerNames.ContainsKey(pMember)) {
                    playerName = _playerNames[pMember];
                }
            } catch {}
            
            base.removeMember(pMember);
            
            // Mark this player as returned to lobby to prevent duplicate addition
            _playerReturnedToLobby[pMember] = true;
            
            // If game is still active and a player left, notify remaining player
            if (IsGameInPlay && !string.IsNullOrEmpty(playerName))
            {
                HandlePlayerDisconnection();
            }
        }

        private void HandlePlayerDisconnection()
        {
            if (!IsGameInPlay) return;
            
            // Cancel any pending timers
            if (_roundTimer != null)
            {
                _roundTimer.Dispose();
                _roundTimer = null;
            }
            
            // Mark game as no longer awaiting clicks
            _awaitingClicks = false;
            
            // If there are still players in the game
            if (_members.Count > 0)
            {
                // Get the disconnected player's info
                string disconnectedPlayerName = "The other player";
                
                // Find which player disconnected by comparing who's left
                if (_members.Count == 1)
                {
                    TcpMessageChannel remainingPlayer = _members[0];
                    
                    foreach (var entry in _playerNames)
                    {
                        if (entry.Key != remainingPlayer)
                        {
                            disconnectedPlayerName = entry.Value;
                            break;
                        }
                    }
                }
                
                // Notify remaining players
                PlayerDisconnectedMessage msg = new PlayerDisconnectedMessage();
                msg.playerName = disconnectedPlayerName;
                sendToAll(msg);
                
                if (!_gameResultBroadcast) {
                    _server.BroadcastGameResultToLobby($"{disconnectedPlayerName} disconnected. Game ended.");
                    _gameResultBroadcast = true;
                }

                // Schedule return to lobby immediately
                ScheduleReturnToLobby(3000); // 3 seconds (reduced from 5)
            }
            else
            {
                // No players left, end the game immediately
                EndGame("Game ended - all players disconnected");
            }
        }

        private void ScheduleReturnToLobby(int delayMs)
        {
            // Simple implementation with a timer thread
            new System.Threading.Timer((_) => {
                ReturnPlayersToLobby();
            }, null, delayMs, System.Threading.Timeout.Infinite);
        }

        private void ReturnPlayersToLobby()
        {
            if (!IsGameInPlay) return;
            
            // Set flag to prevent multiple returns and race conditions
            _returningPlayersToLobby = true;
            
            // Take a snapshot of current players
            TcpMessageChannel[] players = _members.ToArray();
            
            foreach (TcpMessageChannel player in players)
            {
                try
                {
                    // Skip players already returned to lobby
                    if (_playerReturnedToLobby.ContainsKey(player) && _playerReturnedToLobby[player])
                    {
                        continue;
                    }
                    
                    // First check if the player is still connected
                    if (player.IsConnected())
                    {
                        // Mark as returned to prevent duplicate addition
                        _playerReturnedToLobby[player] = true;
                        
                        // Send the room joined event BEFORE removing from game room
                        // This ensures the client receives notification before any room change
                        RoomJoinedEvent roomJoinedEvent = new RoomJoinedEvent();
                        roomJoinedEvent.room = RoomJoinedEvent.Room.LOBBY_ROOM;
                        player.SendMessage(roomJoinedEvent);
                        
                        // Short delay to ensure message is sent before room transition
                        System.Threading.Thread.Sleep(50);
                        
                        // Then transition the player
                        removeMember(player);
                        _server.GetLobbyRoom().AddMember(player);
                        
                        // Update heartbeat time to prevent immediate disconnect check
                        PlayerInfo playerInfo = _server.GetPlayerInfo(player);
                        playerInfo.lastHeartbeatTime = DateTime.Now;
                        playerInfo.heartbeatPending = false;
                    }
                    else
                    {
                        Log.LogInfo("Player disconnected during return to lobby", this);
                        removeAndCloseMember(player);
                    }
                }
                catch (Exception e)
                {
                    Log.LogInfo("Error returning player to lobby: " + e.Message, this);
                    try
                    {
                        if (_members.Contains(player))
                        {
                            removeAndCloseMember(player);
                        }
                    }
                    catch {}
                }
            }
            
            // Reset game state
            IsGameInPlay = false;
            _returningPlayersToLobby = false;
        }

        private void EndGame(string message)
        {
            IsGameInPlay = false;
            _playerReturnedToLobby.Clear();
            _returningPlayersToLobby = false;
            
            // Log game ending
            Log.LogInfo(message, this);
        }

        public override void CheckConnections()
        {
            // Force a thorough check of all members for disconnections
            for (int i = _members.Count - 1; i >= 0; i--)
            {
                if (i < _members.Count) // Safety check
                {
                    try
                    {
                        TcpMessageChannel member = _members[i];
                        if (!member.IsConnected())
                        {
                            Log.LogInfo("CheckConnections found disconnected client in game room", this);
                            PlayerDisconnectedFromGame(member);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.LogInfo("Exception in CheckConnections: " + e.Message, this);
                    }
                }
            }
        }
    }
}