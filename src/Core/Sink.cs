namespace Zoomies.Core;

internal interface IMoveSink
{
    void Quiets(Square from, ulong to);
    void Captures(Square from, ulong to);
    void DoublePushes(Square from, ulong to);
    void PromoQuiets(Square from, ulong to);
    void PromoCaptures(Square from, ulong to);
    void PawnMoves(ulong to, int dir, MoveFlags flag);
    void PawnPromos(ulong to, int dir, bool capture);
    void FromMask(ulong from, Square to, MoveFlags flag);
    void One(Square from, Square to, MoveFlags flag);
}
