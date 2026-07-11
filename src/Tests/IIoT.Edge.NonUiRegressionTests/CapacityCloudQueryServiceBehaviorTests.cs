using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Features.Production.CapacityView;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using MediatR;
using Xunit;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class CapacityCloudQueryServiceBehaviorTests
{
    [Fact]
    public async Task QueryByProductionDay_WhenHourlyRequestFails_ShouldReturnUnavailableWithoutFallback()
    {
        var cloud = new FakeCloudHttpClient();
        cloud.EnqueueGetResult(CloudCallResult<string>.Failure(
            CloudCallOutcome.NetworkFailure,
            "raw_network_detail"));
        var logger = new FakeLogService();
        var service = CreateService(cloud, logger);

        var result = await service.QueryByProductionDayAsync(
            Guid.NewGuid(),
            new DateTime(2026, 7, 11),
            "PLC-01",
            TestContext.Current.CancellationToken);

        Assert.Equal(CapacityQueryState.Unavailable, result.State);
        Assert.Null(result.Value);
        Assert.Single(cloud.GetUrls);
        Assert.Contains(logger.Entries, entry =>
            entry.Message.Contains("原因码=cloud_network_failure", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Message.Contains("raw_network_detail", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryByProductionDay_WhenHourlyJsonIsMalformed_ShouldReturnInvalidPayloadWithoutFallback()
    {
        var cloud = new FakeCloudHttpClient();
        cloud.EnqueueGetResult(CloudCallResult<string>.Success("{malformed"));
        var logger = new FakeLogService();
        var service = CreateService(cloud, logger);

        var result = await service.QueryByProductionDayAsync(
            Guid.NewGuid(),
            new DateTime(2026, 7, 11),
            string.Empty,
            TestContext.Current.CancellationToken);

        Assert.Equal(CapacityQueryState.InvalidPayload, result.State);
        Assert.Null(result.Value);
        Assert.Single(cloud.GetUrls);
        Assert.Contains(logger.Entries, entry =>
            entry.Message.Contains("异常类型=JsonException", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Message.Contains("malformed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryByProductionDay_WhenCloudReturnsContractEmptyValues_ShouldReturnEmpty()
    {
        var cloud = new FakeCloudHttpClient();
        cloud.EnqueueGetResult(CloudCallResult<string>.Success("[]"));
        cloud.EnqueueGetResult(CloudCallResult<string>.Success("[]"));
        cloud.EnqueueGetResult(CloudCallResult<string>.Success("null"));
        cloud.EnqueueGetResult(CloudCallResult<string>.Success("null"));
        var service = CreateService(cloud);

        var result = await service.QueryByProductionDayAsync(
            Guid.NewGuid(),
            new DateTime(2026, 7, 11),
            string.Empty,
            TestContext.Current.CancellationToken);

        Assert.Equal(CapacityQueryState.Empty, result.State);
        Assert.Null(result.Value);
        Assert.Equal(4, cloud.GetCallCount);
    }

    [Fact]
    public async Task QueryByProductionDay_WhenSummaryUsesDeletedArrayContract_ShouldReturnInvalidPayload()
    {
        var cloud = new FakeCloudHttpClient();
        cloud.EnqueueGetResult(CloudCallResult<string>.Success("[]"));
        cloud.EnqueueGetResult(CloudCallResult<string>.Success("[]"));
        cloud.EnqueueGetResult(CloudCallResult<string>.Success("[]"));
        var service = CreateService(cloud);

        var result = await service.QueryByProductionDayAsync(
            Guid.NewGuid(),
            new DateTime(2026, 7, 11),
            string.Empty,
            TestContext.Current.CancellationToken);

        Assert.Equal(CapacityQueryState.InvalidPayload, result.State);
        Assert.Null(result.Value);
        Assert.Equal(3, cloud.GetCallCount);
    }

    [Fact]
    public async Task QueryByProductionDay_WhenHourlyRowsAreValid_ShouldReturnSuccessWithoutSummaryFallback()
    {
        var cloud = new FakeCloudHttpClient();
        cloud.EnqueueGetResult(CloudCallResult<string>.Success(
            """
            [{
              "hour": 8,
              "minute": 30,
              "timeLabel": "08:30-09:00",
              "shiftCode": "D",
              "totalCount": 12,
              "okCount": 11,
              "ngCount": 1
            }]
            """));
        cloud.EnqueueGetResult(CloudCallResult<string>.Success("[]"));
        var service = CreateService(cloud);

        var result = await service.QueryByProductionDayAsync(
            Guid.NewGuid(),
            new DateTime(2026, 7, 11),
            "PLC-01",
            TestContext.Current.CancellationToken);

        Assert.Equal(CapacityQueryState.Success, result.State);
        var row = Assert.Single(result.Value!);
        Assert.Equal(12, row.Total);
        Assert.Equal(11, row.OkCount);
        Assert.Equal(1, row.NgCount);
        Assert.Equal(2, cloud.GetCallCount);
        Assert.All(cloud.GetUrls, url => Assert.Contains("plcName=PLC-01", url, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[{}]")]
    [InlineData("null")]
    [InlineData("[{\"date\":\"2026-07-01\",\"totalCount\":1}]")]
    public async Task QueryByMonth_WhenPayloadViolatesCurrentRangeContract_ShouldReturnInvalidPayload(
        string payload)
    {
        var cloud = new FakeCloudHttpClient();
        cloud.EnqueueGetResult(CloudCallResult<string>.Success(payload));
        var service = CreateService(cloud);

        var result = await service.QueryByMonthAsync(
            Guid.NewGuid(),
            2026,
            7,
            string.Empty,
            TestContext.Current.CancellationToken);

        Assert.Equal(CapacityQueryState.InvalidPayload, result.State);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task QueryByMonth_WhenResponseBodyIsBlank_ShouldReturnUnavailableInsteadOfEmpty()
    {
        var cloud = new FakeCloudHttpClient();
        cloud.EnqueueGetResult(CloudCallResult<string>.Success(string.Empty));
        var service = CreateService(cloud);

        var result = await service.QueryByMonthAsync(
            Guid.NewGuid(),
            2026,
            7,
            string.Empty,
            TestContext.Current.CancellationToken);

        Assert.Equal(CapacityQueryState.Unavailable, result.State);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task QueryByMonth_WhenContractReturnsZeroValuedRow_ShouldRemainSuccess()
    {
        var cloud = new FakeCloudHttpClient();
        cloud.EnqueueGetResult(CloudCallResult<string>.Success(
            """
            [{
              "date": "2026-07-01",
              "totalCount": 0,
              "okCount": 0,
              "ngCount": 0,
              "dayShiftTotal": 0,
              "dayShiftOk": 0,
              "dayShiftNg": 0,
              "nightShiftTotal": 0,
              "nightShiftOk": 0,
              "nightShiftNg": 0
            }]
            """));
        var service = CreateService(cloud);

        var result = await service.QueryByMonthAsync(
            Guid.NewGuid(),
            2026,
            7,
            string.Empty,
            TestContext.Current.CancellationToken);

        Assert.Equal(CapacityQueryState.Success, result.State);
        var row = Assert.Single(result.Value!);
        Assert.Equal(0, row.Total);
    }

    [Fact]
    public async Task QueryByProductionDay_WhenSummaryObjectIsValid_ShouldReturnSuccess()
    {
        var cloud = new FakeCloudHttpClient();
        cloud.EnqueueGetResult(CloudCallResult<string>.Success("[]"));
        cloud.EnqueueGetResult(CloudCallResult<string>.Success("[]"));
        cloud.EnqueueGetResult(CloudCallResult<string>.Success(
            """
            {
              "totalCount": 10,
              "okCount": 9,
              "ngCount": 1,
              "dayShiftTotal": 6,
              "dayShiftOk": 6,
              "dayShiftNg": 0,
              "nightShiftTotal": 4,
              "nightShiftOk": 3,
              "nightShiftNg": 1
            }
            """));
        cloud.EnqueueGetResult(CloudCallResult<string>.Success("null"));
        var service = CreateService(cloud);

        var result = await service.QueryByProductionDayAsync(
            Guid.NewGuid(),
            new DateTime(2026, 7, 11),
            string.Empty,
            TestContext.Current.CancellationToken);

        Assert.Equal(CapacityQueryState.Success, result.State);
        var row = Assert.Single(result.Value!);
        Assert.Equal(10, row.Total);
        Assert.Equal(9, row.OkCount);
        Assert.Equal(1, row.NgCount);
    }

    [Fact]
    public async Task QueryByMonth_WhenCloudClientThrows_ShouldLogOnlySafeExceptionType()
    {
        var cloud = new FakeCloudHttpClient
        {
            GetException = new InvalidOperationException("sensitive response body")
        };
        var logger = new FakeLogService();
        var service = CreateService(cloud, logger);

        var result = await service.QueryByMonthAsync(
            Guid.NewGuid(),
            2026,
            7,
            string.Empty,
            TestContext.Current.CancellationToken);

        Assert.Equal(CapacityQueryState.Unavailable, result.State);
        Assert.Contains(logger.Entries, entry =>
            entry.Message.Contains("异常类型=InvalidOperationException", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Message.Contains("sensitive response body", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryByProductionDay_WhenPathProviderThrows_ShouldRemainNonFatalAndLogSafeType()
    {
        var cloud = new FakeCloudHttpClient();
        var logger = new FakeLogService();
        var service = CreateService(
            cloud,
            logger,
            new FixedCapacityPathProvider(new InvalidOperationException("sensitive path detail")));

        var result = await service.QueryByProductionDayAsync(
            Guid.NewGuid(),
            new DateTime(2026, 7, 11),
            string.Empty,
            TestContext.Current.CancellationToken);

        Assert.Equal(CapacityQueryState.Unavailable, result.State);
        Assert.Equal(0, cloud.GetCallCount);
        Assert.Contains(logger.Entries, entry =>
            entry.Message.Contains("异常类型=InvalidOperationException", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Message.Contains("sensitive path detail", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryByProductionDay_WhenCanceled_ShouldPropagateCancellationWithoutHttpCall()
    {
        var cloud = new FakeCloudHttpClient();
        var service = CreateService(cloud);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.QueryByProductionDayAsync(
                Guid.NewGuid(),
                new DateTime(2026, 7, 11),
                string.Empty,
                cancellation.Token));

        Assert.Equal(0, cloud.GetCallCount);
    }

    [Fact]
    public async Task QueryByProductionDay_WhenCloudClientCancels_ShouldPropagateOriginalCancellation()
    {
        var expected = new OperationCanceledException("cloud request canceled");
        var cloud = new FakeCloudHttpClient { GetException = expected };
        var service = CreateService(cloud);

        var actual = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.QueryByProductionDayAsync(
                Guid.NewGuid(),
                new DateTime(2026, 7, 11),
                string.Empty,
                TestContext.Current.CancellationToken));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task CapacityQueryFacade_WhenGateIsNotReady_ShouldNotSendQuery()
    {
        var sender = new CountingSender();
        var deviceService = new FakeDeviceService
        {
            CanUploadToCloud = false,
            CurrentDevice = new DeviceSession { DeviceId = Guid.NewGuid() }
        };
        var facade = new CapacityQueryFacade(sender, deviceService);

        var result = await facade.LoadTodayAsync(
            "PLC-01",
            TestContext.Current.CancellationToken);

        Assert.Equal(CapacityQueryState.Unavailable, result.State);
        Assert.Equal(0, sender.SendCount);
    }

    [Fact]
    public async Task CapacityQueryFacade_WhenDeviceIdIsMissing_ShouldNotSendQuery()
    {
        var sender = new CountingSender();
        var deviceService = new FakeDeviceService
        {
            CanUploadToCloud = true,
            CurrentDevice = null
        };
        var facade = new CapacityQueryFacade(sender, deviceService);

        var result = await facade.QueryHistoryAsync(
            CapacityQueryModes.Day,
            new DateTime(2026, 7, 11),
            "PLC-01",
            TestContext.Current.CancellationToken);

        Assert.Equal(CapacityQueryState.Unavailable, result.State);
        Assert.Equal(0, sender.SendCount);
    }

    private static CapacityCloudQueryService CreateService(
        FakeCloudHttpClient cloud,
        FakeLogService? logger = null,
        ICloudApiPathProvider? pathProvider = null)
        => new(
            cloud,
            pathProvider ?? new FixedCapacityPathProvider(),
            new ShiftConfig(),
            logger ?? new FakeLogService());

    private sealed class FixedCapacityPathProvider(Exception? exception = null) : ICloudApiPathProvider
    {
        public string GetProcessUploadPath() => "/api/v1/edge/process-records";

        public string GetPassStationBatchPath(string typeKey)
            => $"/api/v1/edge/pass-stations/{typeKey}/batch";

        public string GetCapacityHourlyPath()
            => Resolve("/api/v1/edge/capacity/hourly");

        public string GetCapacitySummaryPath()
            => Resolve("/api/v1/edge/capacity/summary");

        public string GetCapacitySummaryRangePath()
            => Resolve("/api/v1/edge/capacity/summary/range");

        private string Resolve(string path)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return path;
        }
    }

    private sealed class CountingSender : ISender
    {
        public int SendCount { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            throw new NotSupportedException(request.GetType().FullName);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => throw new NotSupportedException(request?.GetType().FullName);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().FullName);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().FullName);

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().FullName);
    }
}
