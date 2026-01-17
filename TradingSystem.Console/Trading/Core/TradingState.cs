namespace TradingSystem.Console.Trading;

/// <summary>
/// Trading State
/// </summary>
public class TradingState
{
    // State Version Control
    public int StateVersion { get; set; } = 1;           // State version, increments on save
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;  // Last updated time
    public string? LastUpdatedBy { get; set; }           // Last updated source (for debugging)
    
    public string Symbol { get; set; } = "";
    public decimal Capital { get; set; }
    public bool IsInPosition { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal EntryCost { get; set; }  // Buying total cost (including fees)
    public decimal CurrentPrice { get; set; }
    public decimal HighestPriceSinceEntry { get; set; }
    public decimal StopLossPrice { get; set; }
    public decimal TodayPnL { get; set; }
    public int TodayTrades { get; set; }
    public int ConsecutiveLosses { get; set; }
    public bool RiskPaused { get; set; }
    public string? RiskPauseReason { get; set; }
    public DateTime? LastTradeTime { get; set; }
    public string CurrentSignal { get; set; } = "";
    
    // Full Risk Control Support Fields
    public decimal PeakCapital { get; set; }          // Peak Capital
    public decimal DayStartCapital { get; set; }      // Daily Start Capital
    public decimal MaxDrawdown { get; set; }          // Max Drawdown
    public DateTime LastTradeDate { get; set; } = DateTime.MinValue;  // Last Trade Date
    
    // Trading Statistics
    public int TotalTradeCount { get; set; }          // Total Trade Count
    public int TodayWinCount { get; set; }            // Today Win Count
    public int TotalWinCount { get; set; }            // Total Win Count
    
    // Order Tracking
    public long? PendingBuyOrderId { get; set; }      // Pending Buy Order ID
    public decimal PendingBuyPrice { get; set; }      // Pending Buy Order Price
    public decimal PendingBuyQuantity { get; set; }   // Pending Buy Order Quantity
    public long? PendingSellOrderId { get; set; }     // Pending Sell Order ID
    public decimal PendingSellPrice { get; set; }     // Pending Sell Order Price
    public decimal PendingSellQuantity { get; set; }  // Pending Sell Order Quantity
    
    // Order Confirmation Mechanism
    public DateTime? LastOrderRequestTime { get; set; }   // Last Order Request Time
    public string? LastOrderRequestSide { get; set; }     // Last Order Request Side (BUY/SELL)
    public decimal LastOrderRequestQuantity { get; set; } // Last Order Request Quantity
    public decimal LastOrderRequestPrice { get; set; }    // Last Order Request Price
    public bool LastOrderConfirmed { get; set; } = true;  // Last Order Confirmed
    public long? LastConfirmedOrderId { get; set; }       // Last Confirmed Order ID
    
    /// <summary>
    /// Daily Reset Check
    /// </summary>
    public void CheckNewDay()
    {
        var today = DateTime.UtcNow.Date;
        if (LastTradeDate.Date < today)
        {
            DayStartCapital = Capital;
            TodayTrades = 0;
            TodayPnL = 0m;
            TodayWinCount = 0;  // Reset today win count
            LastTradeDate = today;
            
            // If paused due to daily loss, auto-resume on new day
            if (RiskPaused && RiskPauseReason?.Contains("Daily") == true)
            {
                RiskPaused = false;
                RiskPauseReason = null;
            }
        }
    }
    
    /// <summary>
    /// Update Peak Capital
    /// </summary>
    public void UpdatePeakCapital()
    {
        if (Capital > PeakCapital)
        {
            PeakCapital = Capital;
        }
        
        // Update Max Drawdown
        if (PeakCapital > 0)
        {
            var currentDrawdown = (PeakCapital - Capital) / PeakCapital;
            if (currentDrawdown > MaxDrawdown)
            {
                MaxDrawdown = currentDrawdown;
            }
        }
    }
}

/// <summary>
/// K-line Data
/// </summary>
public class Candle
{
    public DateTime OpenTime { get; set; }
    public DateTime Time { get => OpenTime; set => OpenTime = value; }  // Alias
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public bool IsClosed { get; set; }
    
    // Indicator Values (filled by indicator calculation)
    public decimal HaOpen { get; set; }
    public decimal HaClose { get; set; }
    public decimal HaHigh { get; set; }
    public decimal HaLow { get; set; }
}

/// <summary>
/// Trading Signal
/// </summary>
public enum TradingSignal
{
    Neutral,
    Bull,
    StrongBull,
    Bear,
    StrongBear
}
