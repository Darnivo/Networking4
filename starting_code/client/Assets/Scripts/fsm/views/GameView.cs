using TMPro;
using UnityEngine;
using UnityEngine.UI;
using shared;

/**
 * Wraps all elements and functionality required for the GameView.
 */
public class GameView : View
{
    [SerializeField] private GameBoard _gameboard = null;
    public GameBoard gameBoard => _gameboard;
    [SerializeField] private TMP_Text _player1Label = null;
    public TMP_Text playerLabel1 => _player1Label;
    [SerializeField] private TMP_Text _player2Label = null;
    public TMP_Text playerLabel2 => _player2Label;

    [SerializeField] private Button _btnConcede = null;
    public Button btnConcede => _btnConcede;

    [SerializeField] private TMP_Text _gameStatus = null;
    public TMP_Text gameStatus => _gameStatus;

    public void ResetBoard()
    {
        // Reset all cells to empty state
        TicTacToeBoardData emptyBoard = new TicTacToeBoardData();
        gameBoard.SetBoardData(emptyBoard);
    }
}

