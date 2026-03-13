[ApiController]
[Route("api/trades")]
[Authorize]
public class TradesController : ControllerBase
{
    private readonly AppDbContext _db;

    public TradesController(AppDbContext db) => _db = db;

    [HttpGet("paper")]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        var trades = await _db.PaperTrades
            .OrderByDescending(t => t.OpenedAt)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync();
        return Ok(trades);
    }

    [HttpGet("paper/open")]
    public async Task<IActionResult> GetOpen() =>
        Ok(await _db.PaperTrades.Where(t => t.Status == TradeStatus.Open)
            .OrderByDescending(t => t.OpenedAt).ToListAsync());

    [HttpGet("paper/{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var trade = await _db.PaperTrades.FindAsync(id);
        return trade is null ? NotFound() : Ok(trade);
    }

    [HttpGet("accuracy")]
    public async Task<IActionResult> GetAccuracy() =>
        Ok(await _db.SignalAccuracies.OrderByDescending(s => s.WinRate).ToListAsync());

    [HttpGet("accuracy/{ticker}")]
    public async Task<IActionResult> GetAccuracyByTicker(string ticker)
    {
        var acc = await _db.SignalAccuracies
            .FirstOrDefaultAsync(s => s.TickerSymbol == ticker.ToUpper());
        return acc is null ? NotFound() : Ok(acc);
    }

    [HttpPost("paper/{id:int}/close")]
    public async Task<IActionResult> ManualClose(int id)
    {
        var trade = await _db.PaperTrades.FindAsync(id);
        if (trade is null || trade.Status != TradeStatus.Open) return BadRequest();
        trade.Status = TradeStatus.Closed;
        trade.ClosedAt = DateTime.UtcNow;
        trade.Outcome = TradeOutcome.Breakeven;
        await _db.SaveChangesAsync();
        return Ok(trade);
    }
}
