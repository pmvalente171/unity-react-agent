using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Demo.ChessDemo
{
    public enum Side
    {
        White, // the player, moves first
        Black, // the AI
    }

    /// <summary>A move in from-to notation: "e2e4", or "e7e8q" for a promotion. Squares are 0..63 = a1..h8.</summary>
    public readonly struct Move
    {
        public readonly int From, To;
        public readonly char Promotion; // 'q', 'r', 'b', 'n' or '\0'

        public Move(int from, int to, char promotion = '\0')
        {
            From = from;
            To = to;
            Promotion = promotion;
        }

        public override string ToString() => ChessBoard.Name(From) + ChessBoard.Name(To) + (Promotion == '\0' ? "" : Promotion.ToString());
    }

    /// <summary>
    /// Chess rules as plain data: legal moves (castling, en passant, promotion, no moving into check),
    /// checkmate and stalemate. Pieces are FEN letters (uppercase White, lowercase Black, '.' empty).
    /// Not covered: draws by repetition, the 50-move rule or insufficient material.
    /// Boards are immutable from the outside: <see cref="Apply"/> returns a new one.
    /// </summary>
    public sealed class ChessBoard
    {
        private static readonly (int f, int r)[] KnightJumps = { (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2) };
        private static readonly (int f, int r)[] Diagonals = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
        private static readonly (int f, int r)[] Straights = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        private static readonly (int f, int r)[] AllDirections = Diagonals.Concat(Straights).ToArray();

        private readonly char[] _squares = new char[64];
        private string _castling = "KQkq";
        private int _enPassant = -1; // the square a pawn just skipped, or -1

        public Side Turn { get; private set; }
        public char this[int square] => _squares[square];

        public static ChessBoard Start()
        {
            var board = new ChessBoard();
            const string back = "rnbqkbnr";
            for (var f = 0; f < 8; f++)
            {
                board._squares[f] = char.ToUpper(back[f]);
                board._squares[8 + f] = 'P';
                for (var r = 2; r < 6; r++) board._squares[r * 8 + f] = '.';
                board._squares[48 + f] = 'p';
                board._squares[56 + f] = back[f];
            }

            return board;
        }

        public static string Name(int square) => $"{(char)('a' + square % 8)}{square / 8 + 1}";
        public static Side Other(Side side) => side == Side.White ? Side.Black : Side.White;
        public static Side? Owner(char piece) => piece == '.' ? null : char.IsUpper(piece) ? Side.White : Side.Black;

        public static string PieceName(char piece) => char.ToLower(piece) switch
        {
            'p' => "pawn", 'n' => "knight", 'b' => "bishop", 'r' => "rook", 'q' => "queen", 'k' => "king", _ => "nothing",
        };

        public bool InCheck(Side side) => IsAttacked(Array.IndexOf(_squares, Piece('k', side)), Other(side));

        public List<Move> LegalMoves() => PseudoMoves().Where(m => !Apply(m).InCheck(Turn)).ToList();

        /// <summary>Finds a legal move by notation; a promotion without a letter becomes a queen.</summary>
        public Move? FindMove(string notation)
        {
            var wanted = (notation ?? "").Trim().ToLowerInvariant().Replace("-", "").Replace(" ", "");
            foreach (var move in LegalMoves())
            {
                var name = move.ToString();
                if (name == wanted || (move.Promotion == 'q' && name == wanted + "q")) return move;
            }

            return null;
        }

        public ChessBoard Apply(Move move)
        {
            var next = new ChessBoard { Turn = Other(Turn), _castling = _castling };
            Array.Copy(_squares, next._squares, 64);

            var piece = _squares[move.From];
            var kind = char.ToLower(piece);
            next._squares[move.From] = '.';
            next._squares[move.To] = move.Promotion == '\0' ? piece : Piece(move.Promotion, Turn);

            if (kind == 'p' && move.To == _enPassant) next._squares[move.From / 8 * 8 + move.To % 8] = '.';
            if (kind == 'p' && Math.Abs(move.To - move.From) == 16) next._enPassant = (move.From + move.To) / 2;
            if (kind == 'k' && Math.Abs(move.To - move.From) == 2) // castling: move the rook too
            {
                var (rookFrom, rookTo) = move.To > move.From ? (move.From + 3, move.From + 1) : (move.From - 4, move.From - 1);
                next._squares[rookTo] = next._squares[rookFrom];
                next._squares[rookFrom] = '.';
            }

            // Moving the king, or moving / capturing a rook on its corner, loses those castling rights.
            var lost = kind == 'k' ? (Turn == Side.White ? "KQ" : "kq") : "";
            foreach (var (corner, right) in new[] { (0, 'Q'), (7, 'K'), (56, 'q'), (63, 'k') })
                if (move.From == corner || move.To == corner) lost += right;
            next._castling = new string(_castling.Where(c => !lost.Contains(c)).ToArray());
            return next;
        }

        /// <summary>The board as text for the model: rank 8 at the top.</summary>
        public string ToText()
        {
            var text = new StringBuilder("  a b c d e f g h\n");
            for (var r = 7; r >= 0; r--)
            {
                text.Append(r + 1).Append(' ');
                for (var f = 0; f < 8; f++) text.Append(_squares[r * 8 + f]).Append(' ');
                text.Append(r + 1).Append('\n');
            }

            return text.Append("  a b c d e f g h").ToString();
        }

        public string DescribeMaterial()
        {
            int Score(Side side) => _squares.Where(p => Owner(p) == side)
                .Sum(p => char.ToLower(p) switch { 'p' => 1, 'n' => 3, 'b' => 3, 'r' => 5, 'q' => 9, _ => 0 });
            return $"Material: White {Score(Side.White)}, Black {Score(Side.Black)} (pawn 1, knight/bishop 3, rook 5, queen 9).";
        }

        /// <summary>Legal moves grouped by piece, with captures and checks marked, for the model.</summary>
        public string DescribeMoves()
        {
            var lines = LegalMoves().GroupBy(m => m.From).Select(group =>
                $"{PieceName(_squares[group.Key])} {Name(group.Key)}: " + string.Join(", ", group.Select(Annotate)));
            return string.Join("\n", lines);
        }

        /// <summary>Null while the game goes on, otherwise how it ended.</summary>
        public string Result()
        {
            if (LegalMoves().Count > 0) return null;
            return InCheck(Turn) ? $"Checkmate: {Other(Turn)} wins." : "Stalemate: it's a draw.";
        }

        private string Annotate(Move move)
        {
            var notes = new List<string>();
            var target = _squares[move.To];
            if (target != '.') notes.Add("takes " + PieceName(target));
            else if (char.ToLower(_squares[move.From]) == 'p' && move.To == _enPassant) notes.Add("takes pawn en passant");
            if (Apply(move).InCheck(Other(Turn))) notes.Add("check");
            return notes.Count == 0 ? move.ToString() : $"{move} ({string.Join(", ", notes)})";
        }

        private IEnumerable<Move> PseudoMoves()
        {
            for (var from = 0; from < 64; from++)
            {
                var piece = _squares[from];
                if (Owner(piece) != Turn) continue;
                int f = from % 8, r = from / 8;

                switch (char.ToLower(piece))
                {
                    case 'p':
                        var dir = Turn == Side.White ? 1 : -1;
                        var lastRank = Turn == Side.White ? 7 : 0;
                        if (At(f, r + dir) == '.')
                        {
                            foreach (var m in PawnMoves(from, from + dir * 8, r + dir == lastRank)) yield return m;
                            if (r == (Turn == Side.White ? 1 : 6) && At(f, r + 2 * dir) == '.') yield return new Move(from, from + dir * 16);
                        }

                        foreach (var df in new[] { -1, 1 })
                        {
                            var to = (r + dir) * 8 + f + df;
                            var target = At(f + df, r + dir);
                            if (target != '\0' && (Owner(target) == Other(Turn) || to == _enPassant))
                                foreach (var m in PawnMoves(from, to, r + dir == lastRank)) yield return m;
                        }

                        break;
                    case 'n': foreach (var m in Steps(from, KnightJumps)) yield return m; break;
                    case 'b': foreach (var m in Slides(from, Diagonals)) yield return m; break;
                    case 'r': foreach (var m in Slides(from, Straights)) yield return m; break;
                    case 'q': foreach (var m in Slides(from, AllDirections)) yield return m; break;
                    case 'k':
                        foreach (var m in Steps(from, AllDirections)) yield return m;
                        foreach (var m in Castles(from)) yield return m;
                        break;
                }
            }
        }

        private static IEnumerable<Move> PawnMoves(int from, int to, bool promotes) =>
            promotes ? "qrbn".Select(p => new Move(from, to, p)) : new[] { new Move(from, to) };

        private IEnumerable<Move> Steps(int from, (int f, int r)[] offsets)
        {
            foreach (var (df, dr) in offsets)
            {
                var target = At(from % 8 + df, from / 8 + dr);
                if (target != '\0' && Owner(target) != Turn) yield return new Move(from, from + dr * 8 + df);
            }
        }

        private IEnumerable<Move> Slides(int from, (int f, int r)[] directions)
        {
            foreach (var (df, dr) in directions)
            for (int f = from % 8 + df, r = from / 8 + dr; At(f, r) != '\0'; f += df, r += dr)
            {
                var target = At(f, r);
                if (Owner(target) == Turn) break;
                yield return new Move(from, r * 8 + f);
                if (target != '.') break;
            }
        }

        private IEnumerable<Move> Castles(int from)
        {
            var home = Turn == Side.White ? 4 : 60;
            var enemy = Other(Turn);
            if (from != home || IsAttacked(home, enemy)) yield break;

            var (kingSide, queenSide) = Turn == Side.White ? ('K', 'Q') : ('k', 'q');
            if (_castling.Contains(kingSide) && _squares[home + 1] == '.' && _squares[home + 2] == '.' &&
                !IsAttacked(home + 1, enemy) && !IsAttacked(home + 2, enemy))
                yield return new Move(home, home + 2);
            if (_castling.Contains(queenSide) && _squares[home - 1] == '.' && _squares[home - 2] == '.' && _squares[home - 3] == '.' &&
                !IsAttacked(home - 1, enemy) && !IsAttacked(home - 2, enemy))
                yield return new Move(home, home - 2);
        }

        private bool IsAttacked(int square, Side by)
        {
            int f = square % 8, r = square / 8;
            var pawnRank = by == Side.White ? r - 1 : r + 1;
            if (At(f - 1, pawnRank) == Piece('p', by) || At(f + 1, pawnRank) == Piece('p', by)) return true;
            if (KnightJumps.Any(o => At(f + o.f, r + o.r) == Piece('n', by))) return true;
            if (AllDirections.Any(o => At(f + o.f, r + o.r) == Piece('k', by))) return true;
            return Diagonals.Any(d => FirstPiece(f, r, d) is var p && (p == Piece('b', by) || p == Piece('q', by))) ||
                   Straights.Any(d => FirstPiece(f, r, d) is var p && (p == Piece('r', by) || p == Piece('q', by)));
        }

        private char FirstPiece(int f, int r, (int f, int r) d)
        {
            for (f += d.f, r += d.r; At(f, r) != '\0'; f += d.f, r += d.r)
                if (At(f, r) != '.') return At(f, r);
            return '.';
        }

        private char At(int f, int r) => f is >= 0 and < 8 && r is >= 0 and < 8 ? _squares[r * 8 + f] : '\0';
        private static char Piece(char kind, Side side) => side == Side.White ? char.ToUpper(kind) : char.ToLower(kind);
    }
}
