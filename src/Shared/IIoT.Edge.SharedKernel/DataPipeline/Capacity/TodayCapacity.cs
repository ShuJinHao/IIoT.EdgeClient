namespace IIoT.Edge.SharedKernel.DataPipeline.Capacity;

public class TodayCapacity
{
    private readonly object _sync = new();

    public string Date { get; set; } = string.Empty;
    public ShiftCapacity DayShift { get; set; } = new("D");
    public ShiftCapacity NightShift { get; set; } = new("N");
    public List<HalfHourCapacity> HalfHourly { get; set; } = CreateHalfHourBuckets();

    public int TotalAll
    {
        get
        {
            lock (_sync)
            {
                return DayShift.Total + NightShift.Total;
            }
        }
    }

    public int OkAll
    {
        get
        {
            lock (_sync)
            {
                return DayShift.OkCount + NightShift.OkCount;
            }
        }
    }

    public int NgAll
    {
        get
        {
            lock (_sync)
            {
                return DayShift.NgCount + NightShift.NgCount;
            }
        }
    }

    public string YieldAll
    {
        get
        {
            lock (_sync)
            {
                var total = DayShift.Total + NightShift.Total;
                var ok = DayShift.OkCount + NightShift.OkCount;
                return total > 0 ? $"{ok * 100.0 / total:F1}%" : "0%";
            }
        }
    }

    public string Increment(DateTime completedTime, bool isOk, TimeSpan dayStart, TimeSpan dayEnd)
    {
        lock (_sync)
        {
            var productionDate = completedTime.TimeOfDay < dayStart
                ? completedTime.Date.AddDays(-1)
                : completedTime.Date;

            var currentDate = productionDate.ToString("yyyy-MM-dd");
            if (Date != currentDate)
            {
                ResetCore();
                Date = currentDate;
            }

            var isDayShift = completedTime.TimeOfDay >= dayStart && completedTime.TimeOfDay < dayEnd;
            var shift = isDayShift ? DayShift : NightShift;

            if (isOk)
            {
                shift.OkCount++;
            }
            else
            {
                shift.NgCount++;
            }

            if (HalfHourly.Count == 0)
            {
                HalfHourly = CreateHalfHourBuckets();
            }

            var slotIndex = completedTime.Hour * 2 + (completedTime.Minute >= 30 ? 1 : 0);
            var bucket = HalfHourly.FirstOrDefault(x => x.SlotIndex == slotIndex);
            if (bucket is null)
            {
                bucket = CreateBucket(slotIndex);
                HalfHourly.Add(bucket);
                HalfHourly = HalfHourly.OrderBy(x => x.SlotIndex).ToList();
            }

            if (isOk)
            {
                bucket.OkCount++;
            }
            else
            {
                bucket.NgCount++;
            }

            return isDayShift ? "D" : "N";
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            ResetCore();
        }
    }

    public TodayCapacity CreateSnapshot()
    {
        lock (_sync)
        {
            return new TodayCapacity
            {
                Date = Date,
                DayShift = DayShift.Clone(),
                NightShift = NightShift.Clone(),
                HalfHourly = HalfHourly.Select(x => x.Clone()).ToList()
            };
        }
    }

    private void ResetCore()
    {
        DayShift = new ShiftCapacity("D");
        NightShift = new ShiftCapacity("N");
        HalfHourly = CreateHalfHourBuckets();
    }

    private static List<HalfHourCapacity> CreateHalfHourBuckets()
        => Enumerable.Range(0, 48)
            .Select(CreateBucket)
            .ToList();

    private static HalfHourCapacity CreateBucket(int slotIndex)
    {
        var startHour = slotIndex / 2;
        var startMinute = slotIndex % 2 == 0 ? 0 : 30;
        var endTotalMinutes = startHour * 60 + startMinute + 30;
        var endHour = (endTotalMinutes / 60) % 24;
        var endMinute = endTotalMinutes % 60;

        return new HalfHourCapacity
        {
            SlotIndex = slotIndex,
            StartHour = startHour,
            StartMinute = startMinute,
            EndHour = endHour,
            EndMinute = endMinute
        };
    }
}

public class ShiftCapacity
{
    public ShiftCapacity() { }

    public ShiftCapacity(string shiftCode)
    {
        ShiftCode = shiftCode;
    }

    public string ShiftCode { get; set; } = string.Empty;
    public int OkCount { get; set; }
    public int NgCount { get; set; }

    public int Total => OkCount + NgCount;
    public string Yield => Total > 0 ? $"{OkCount * 100.0 / Total:F1}%" : "0%";

    public ShiftCapacity Clone()
        => new(ShiftCode)
        {
            OkCount = OkCount,
            NgCount = NgCount
        };
}

public class HalfHourCapacity
{
    public int SlotIndex { get; set; }
    public int StartHour { get; set; }
    public int StartMinute { get; set; }
    public int EndHour { get; set; }
    public int EndMinute { get; set; }
    public int OkCount { get; set; }
    public int NgCount { get; set; }

    public int Total => OkCount + NgCount;
    public string Yield => Total > 0 ? $"{OkCount * 100.0 / Total:F1}%" : "0%";
    public string TimeLabel => $"{StartHour:D2}:{StartMinute:D2}-{EndHour:D2}:{EndMinute:D2}";

    public HalfHourCapacity Clone()
        => new()
        {
            SlotIndex = SlotIndex,
            StartHour = StartHour,
            StartMinute = StartMinute,
            EndHour = EndHour,
            EndMinute = EndMinute,
            OkCount = OkCount,
            NgCount = NgCount
        };
}
