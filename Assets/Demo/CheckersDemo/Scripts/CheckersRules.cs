using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Demo.CheckersDemo
{
    public enum Side
    {
        Black, // the player: starts on ranks 1-3, moves up
        White, // the AI: starts on ranks 6-8, moves down
    }

    public enum Piece : byte
    {
        Empty,
        Black,
        BlackKing,
        White,
        WhiteKing,
    }

    /// <summary>One move: the squares visited (two for a step, more for multi-jumps) and the pieces taken.</summary>
    public sealed class Move
    {
        public readonly Vector2Int[] Path;
        public readonly Vector2Int[] Captured;

        public Move(IEnumerable<Vector2Int> path, IEnumerable<Vector2Int> captured)
        {
            Path = path.ToArray();
            Captured = captured.ToArray();
        }

        public Vector2Int From => Path[0];
        public Vector2Int To => Path[^1];

        /// <summary>"c3-d4" for a step, "c3xe5xg7" for jumps.</summary>
        public override string ToString() => string.Join(Captured.Length > 0 ? "x" : "-", Path.Select(Board.Name));
    }

    /// <summary>
    /// American checkers on an 8x8 board, as plain data: captures are mandatory, multi-jumps continue,
    /// a man that reaches the far rank becomes a king (and its move ends there). Squares are (file, rank)
    /// from (0,0) = a1, a dark square. Boards are immutable from the outside: <see cref="Apply"/> returns a new one.
    /// </summary>
    public sealed class Board
    {
        private static readonly Vector2Int[] AllDirections = { new(1, 1), new(-1, 1), new(1, -1), new(-1, -1) };

        private readonly Piece[,] _cells = new Piece[8, 8];

        public Piece this[Vector2Int square] => _cells[square.x, square.y];

        public static Board Start()
        {
            var board = new Board();
            for (var rank = 0; rank < 8; rank++)
            for (var file = 0; file < 8; file++)
            {
                if (!IsDark(file, rank)) continue;
                if (rank < 3) board._cells[file, rank] = Piece.Black;
                else if (rank > 4) board._cells[file, rank] = Piece.White;
            }

            return board;
        }

        public static bool IsDark(int file, int rank) => (file + rank) % 2 == 0;
        public static bool InBounds(Vector2Int s) => s.x is >= 0 and < 8 && s.y is >= 0 and < 8;
        public static string Name(Vector2Int s) => $"{(char)('a' + s.x)}{s.y + 1}";

        public static Side? Owner(Piece piece) => piece switch
        {
            Piece.Black or Piece.BlackKing => Side.Black,
            Piece.White or Piece.WhiteKing => Side.White,
            _ => null,
        };

        public static bool IsKing(Piece piece) => piece is Piece.BlackKing or Piece.WhiteKing;
        public static Side Other(Side side) => side == Side.Black ? Side.White : Side.Black;

        public int Count(Side side, bool kingsOnly = false)
        {
            var count = 0;
            foreach (var piece in _cells)
                if (Owner(piece) == side && (!kingsOnly || IsKing(piece)))
                    count++;
            return count;
        }

        /// <summary>Every legal move for <paramref name="side"/>; only captures when any capture exists.</summary>
        public List<Move> LegalMoves(Side side)
        {
            var jumps = new List<Move>();
            var steps = new List<Move>();
            for (var rank = 0; rank < 8; rank++)
            for (var file = 0; file < 8; file++)
            {
                var from = new Vector2Int(file, rank);
                var piece = this[from];
                if (Owner(piece) != side) continue;

                _cells[file, rank] = Piece.Empty; // the moving piece leaves its square (a king may jump back over it)
                FindJumps(from, piece, new List<Vector2Int> { from }, new List<Vector2Int>(), jumps);
                _cells[file, rank] = piece;

                foreach (var dir in Directions(piece))
                {
                    var to = from + dir;
                    if (InBounds(to) && this[to] == Piece.Empty) steps.Add(new Move(new[] { from, to }, new Vector2Int[0]));
                }
            }

            return jumps.Count > 0 ? jumps : steps;
        }

        /// <summary>Finds a legal move by its notation ("c3-d4", "c3xe5"), or null.</summary>
        public Move FindMove(Side side, string notation)
        {
            var wanted = Normalize(notation);
            return LegalMoves(side).FirstOrDefault(m => Normalize(m.ToString()) == wanted);
        }

        public Board Apply(Move move)
        {
            var next = new Board();
            System.Array.Copy(_cells, next._cells, _cells.Length);

            var piece = this[move.From];
            next._cells[move.From.x, move.From.y] = Piece.Empty;
            foreach (var taken in move.Captured) next._cells[taken.x, taken.y] = Piece.Empty;

            if (piece == Piece.Black && move.To.y == 7) piece = Piece.BlackKing;
            if (piece == Piece.White && move.To.y == 0) piece = Piece.WhiteKing;
            next._cells[move.To.x, move.To.y] = piece;
            return next;
        }

        /// <summary>The board as text for the model: rank 8 at the top, b/B = black man/king, w/W = white.</summary>
        public string ToText()
        {
            var text = new StringBuilder("  a b c d e f g h\n");
            for (var rank = 7; rank >= 0; rank--)
            {
                text.Append(rank + 1).Append(' ');
                for (var file = 0; file < 8; file++)
                {
                    var piece = _cells[file, rank];
                    var c = piece switch
                    {
                        Piece.Black => 'b', Piece.BlackKing => 'B', Piece.White => 'w', Piece.WhiteKing => 'W',
                        _ => IsDark(file, rank) ? '.' : ' ',
                    };
                    text.Append(c).Append(' ');
                }

                text.Append(rank + 1).Append('\n');
            }

            return text.Append("  a b c d e f g h").ToString();
        }

        public string DescribeMaterial() =>
            $"White: {Count(Side.White)} pieces ({Count(Side.White, true)} kings). " +
            $"Black: {Count(Side.Black)} pieces ({Count(Side.Black, true)} kings).";

        private void FindJumps(Vector2Int at, Piece piece, List<Vector2Int> path, List<Vector2Int> captured, List<Move> result)
        {
            var side = Owner(piece);
            var extended = false;
            foreach (var dir in Directions(piece))
            {
                var over = at + dir;
                var to = over + dir;
                if (!InBounds(to) || this[to] != Piece.Empty || captured.Contains(over)) continue;
                var victim = Owner(this[over]);
                if (victim == null || victim == side) continue;

                extended = true;
                path.Add(to);
                captured.Add(over);
                var crowned = !IsKing(piece) && to.y == (side == Side.Black ? 7 : 0);
                if (crowned) result.Add(new Move(path, captured)); // reaching the king row ends the move
                else FindJumps(to, piece, path, captured, result);
                path.RemoveAt(path.Count - 1);
                captured.RemoveAt(captured.Count - 1);
            }

            if (!extended && captured.Count > 0) result.Add(new Move(path, captured));
        }

        private static IEnumerable<Vector2Int> Directions(Piece piece) => piece switch
        {
            Piece.Black => AllDirections.Take(2),
            Piece.White => AllDirections.Skip(2),
            _ => AllDirections,
        };

        private static string Normalize(string notation) =>
            (notation ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace('x', '-');
    }
}
