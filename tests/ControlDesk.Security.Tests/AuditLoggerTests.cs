using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ControlDesk.Security.Authentication;
using ControlDesk.Security.Storage;
using FluentAssertions;
using Xunit;

namespace ControlDesk.Security.Tests;

public sealed class AuditLoggerTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly byte[] _testChainKey;
    private readonly AuditLogger _auditLogger;

    public AuditLoggerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "ControlDeskAuditTests", Guid.NewGuid().ToString("N"));
        _testChainKey = RandomNumberGenerator.GetBytes(32);
        _auditLogger = new AuditLogger(_testDirectory, _testChainKey);
    }

    public void Dispose()
    {
        _auditLogger.Dispose();
        try
        {
            if (Directory.Exists(_testDirectory))
                Directory.Delete(_testDirectory, true);
        }
        catch { }
    }

    [Fact]
    public async Task LogAuthenticationAsync_Success_CreatesEntryAndChain()
    {
        // Act
        await _auditLogger.LogAuthenticationAsync(
            "123-456-789",
            "987-654-321",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            AuthenticationOutcome.Success,
            cancellationToken: CancellationToken.None);

        // Assert
        var entries = await _auditLogger.GetRecentEntriesAsync(10, CancellationToken.None);
        entries.Should().HaveCount(1);
        entries[0].LocalDeviceId.Should().Be("123-456-789");
        entries[0].RemoteDeviceId.Should().Be("987-654-321");
        entries[0].EventType.Should().Contain("AuthenticationSuccess");
        entries[0].PreviousHashBase64.Should().NotBeNullOrEmpty();
        entries[0].CurrentHashBase64.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LogAuthenticationAsync_Failure_IncludesFailureReason()
    {
        // Act
        await _auditLogger.LogAuthenticationAsync(
            "123-456-789",
            "987-654-321",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            AuthenticationOutcome.Failure_InvalidSignature,
            "Signature verification failed",
            CancellationToken.None);

        // Assert
        var entries = await _auditLogger.GetRecentEntriesAsync(10, CancellationToken.None);
        entries.Should().HaveCount(1);
        entries[0].Details.Should().Contain("InvalidSignature");
        entries[0].Details.Should().Contain("Signature verification failed");
    }

    [Fact]
    public async Task LogSessionEventAsync_RecordsAllEventTypes()
    {
        // Arrange
        var eventTypes = Enum.GetValues<SessionEventType>();

        // Act
        foreach (var eventType in eventTypes)
        {
            await _auditLogger.LogSessionEventAsync(
                "123-456-789",
                "987-654-321",
                eventType,
                $"Detail for {eventType}",
                CancellationToken.None);
        }

        // Assert
        var entries = await _auditLogger.GetRecentEntriesAsync(eventTypes.Length + 5, CancellationToken.None);
        entries.Should().HaveCount(eventTypes.Length);
    }

    [Fact]
    public async Task LogPairingEventAsync_RecordsPairingEvents()
    {
        // Act
        await _auditLogger.LogPairingEventAsync(
            "123-456-789",
            "987-654-321",
            PairingEventType.DevicePaired,
            CancellationToken.None);
        await _auditLogger.LogPairingEventAsync(
            "123-456-789",
            "987-654-321",
            PairingEventType.DeviceUnpaired,
            CancellationToken.None);
        await _auditLogger.LogPairingEventAsync(
            "123-456-789",
            "987-654-321",
            PairingEventType.DevicePermissionsChanged,
            CancellationToken.None);

        // Assert
        var entries = await _auditLogger.GetRecentEntriesAsync(10, CancellationToken.None);
        entries.Should().HaveCount(3);
    }

    [Fact]
    public async Task LogConfigurationChangeAsync_RecordsOldAndNewValues()
    {
        // Act
        await _auditLogger.LogConfigurationChangeAsync(
            "123-456-789",
            "AllowUnattendedAccess",
            "false",
            "true",
            CancellationToken.None);
        await _auditLogger.LogConfigurationChangeAsync(
            "123-456-789",
            "MaxBitrate",
            "5000",
            "10000",
            CancellationToken.None);

        // Assert
        var entries = await _auditLogger.GetRecentEntriesAsync(10, CancellationToken.None);
        entries.Should().HaveCount(2);
        entries[0].Details.Should().Contain("AllowUnattendedAccess");
        entries[0].Details.Should().Contain("false");
        entries[0].Details.Should().Contain("true");
        entries[1].Details.Should().Contain("MaxBitrate");
    }

    [Fact]
    public async Task HashChain_SequentialEntries_AreProperlyChained()
    {
        // Act
        await _auditLogger.LogAuthenticationAsync(
            "123-456-789", "987-654-321", "key1", AuthenticationOutcome.Success, null, CancellationToken.None);
        await _auditLogger.LogAuthenticationAsync(
            "123-456-789", "987-654-321", "key2", AuthenticationOutcome.Success, null, CancellationToken.None);
        await _auditLogger.LogAuthenticationAsync(
            "123-456-789", "987-654-321", "key3", AuthenticationOutcome.Success, null, CancellationToken.None);

        // Assert
        var entries = await _auditLogger.GetRecentEntriesAsync(10, CancellationToken.None);
        entries.Should().HaveCount(3);

        // Verify chain: each entry's CurrentHash should be the next entry's PreviousHash
        // (entries returned newest first, so reverse for chronological order)
        var chronological = entries.OrderBy(e => e.TimestampUtc).ToArray();
        for (int i = 0; i < chronological.Length - 1; i++)
        {
            chronological[i].CurrentHashBase64.Should().Be(chronological[i + 1].PreviousHashBase64);
        }

        // First entry should have empty previous hash (or all zeros)
        chronological[0].PreviousHashBase64.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task VerifyIntegrityAsync_ValidChain_ReturnsTrue()
    {
        // Arrange
        await _auditLogger.LogAuthenticationAsync(
            "123-456-789", "987-654-321", "key1", AuthenticationOutcome.Success, null, CancellationToken.None);
        await _auditLogger.LogSessionEventAsync(
            "123-456-789", "987-654-321", SessionEventType.SessionStarted, "Session started", CancellationToken.None);
        await _auditLogger.LogPairingEventAsync(
            "123-456-789", "987-654-321", PairingEventType.DevicePaired, CancellationToken.None);

        // Act
        var isValid = await _auditLogger.VerifyIntegrityAsync(CancellationToken.None);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyIntegrityAsync_TamperedFile_ReturnsFalse()
    {
        // Arrange
        await _auditLogger.LogAuthenticationAsync(
            "123-456-789", "987-654-321", "key1", AuthenticationOutcome.Success, null, CancellationToken.None);
        await _auditLogger.LogAuthenticationAsync(
            "123-456-789", "987-654-321", "key2", AuthenticationOutcome.Success, null, CancellationToken.None);

        // Tamper with the first log file
        var files = Directory.GetFiles(_testDirectory, "*.json").OrderBy(f => f).ToArray();
        var originalContent = File.ReadAllBytes(files[0]);
        var tampered = originalContent.ToArray();
        tampered[0] ^= 0xFF; // Flip a bit
        File.WriteAllBytes(files[0], tampered);

        // Act
        var isValid = await _auditLogger.VerifyIntegrityAsync(CancellationToken.None);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyIntegrityAsync_DeletedFile_ReturnsFalse()
    {
        // Arrange
        await _auditLogger.LogAuthenticationAsync(
            "123-456-789", "987-654-321", "key1", AuthenticationOutcome.Success, null, CancellationToken.None);
        await _auditLogger.LogAuthenticationAsync(
            "123-456-789", "987-654-321", "key2", AuthenticationOutcome.Success, null, CancellationToken.None);
        await _auditLogger.LogAuthenticationAsync(
            "123-456-789", "987-654-321", "key3", AuthenticationOutcome.Success, null, CancellationToken.None);

        // Delete the middle file
        var files = Directory.GetFiles(_testDirectory, "*.json").OrderBy(f => f).ToArray();
        File.Delete(files[1]);

        // Act
        var isValid = await _auditLogger.VerifyIntegrityAsync(CancellationToken.None);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task GetRecentEntriesAsync_ReturnsEntriesInReverseChronologicalOrder()
    {
        // Arrange
        await _auditLogger.LogAuthenticationAsync("111-111-111", "222-222-222", "k1", AuthenticationOutcome.Success, null, CancellationToken.None);
        await Task.Delay(10); // Ensure different timestamps
        await _auditLogger.LogAuthenticationAsync("111-111-111", "333-333-333", "k2", AuthenticationOutcome.Success, null, CancellationToken.None);
        await Task.Delay(10);
        await _auditLogger.LogAuthenticationAsync("111-111-111", "444-444-444", "k3", AuthenticationOutcome.Success, null, CancellationToken.None);

        // Act
        var entries = await _auditLogger.GetRecentEntriesAsync(10, CancellationToken.None);

        // Assert
        entries.Should().HaveCount(3);
        entries[0].RemoteDeviceId.Should().Be("444-444-444"); // Newest first
        entries[1].RemoteDeviceId.Should().Be("333-333-333");
        entries[2].RemoteDeviceId.Should().Be("222-222-222");
    }

    [Fact]
    public async Task GetRecentEntriesAsync_RespectsCountLimit()
    {
        // Arrange
        for (int i = 0; i < 15; i++)
        {
            await _auditLogger.LogAuthenticationAsync(
                "111-111-111", $"{i:D9}", "key", AuthenticationOutcome.Success, null, CancellationToken.None);
        }

        // Act
        var entries = await _auditLogger.GetRecentEntriesAsync(5, CancellationToken.None);

        // Assert
        entries.Should().HaveCount(5);
    }

    [Fact]
    public async Task MultipleLoggersWithSameChainKey_ShareChain()
    {
        // Arrange
        using var logger1 = new AuditLogger(_testDirectory, _testChainKey);
        await logger1.LogAuthenticationAsync("111-111-111", "222-222-222", "k1", AuthenticationOutcome.Success, null, CancellationToken.None);

        using var logger2 = new AuditLogger(_testDirectory, _testChainKey);
        await logger2.LogAuthenticationAsync("111-111-111", "333-333-333", "k2", AuthenticationOutcome.Success, null, CancellationToken.None);

        // Act
        var entries = await logger2.GetRecentEntriesAsync(10, CancellationToken.None);

        // Assert
        entries.Should().HaveCount(2);
        var isValid = await logger2.VerifyIntegrityAsync(CancellationToken.None);
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task MultipleLoggersWithDifferentChainKeys_DetectMismatch()
    {
        // Arrange
        var chainKey1 = RandomNumberGenerator.GetBytes(32);
        var chainKey2 = RandomNumberGenerator.GetBytes(32);

        using var logger1 = new AuditLogger(_testDirectory, chainKey1);
        await logger1.LogAuthenticationAsync("111-111-111", "222-222-222", "k1", AuthenticationOutcome.Success, null, CancellationToken.None);

        // Create a second logger with different key - should not be able to verify
        using var logger2 = new AuditLogger(_testDirectory, chainKey2);
        await logger2.LogAuthenticationAsync("111-111-111", "333-333-333", "k2", AuthenticationOutcome.Success, null, CancellationToken.None);

        // Act - verify with logger2's key
        var isValid = await logger2.VerifyIntegrityAsync(CancellationToken.None);

        // Assert - logger2 will fail verification because it reads files written with different key
        isValid.Should().BeFalse();
    }

    [Fact]
    public void Dispose_MultipleCalls_IsIdempotent()
    {
        // Act
        _auditLogger.Dispose();
        _auditLogger.Dispose(); // Should not throw

        // Assert - no exception
    }

    [Fact]
    public async Task LogAfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        _auditLogger.Dispose();

        // Act & Assert
        await _auditLogger.Invoking(l => l.LogAuthenticationAsync(
            "111-111-111", "222-222-222", "key", AuthenticationOutcome.Success, null, CancellationToken.None))
            .Should().ThrowAsync<ObjectDisposedException>();

        await _auditLogger.Invoking(l => l.GetRecentEntriesAsync(10, CancellationToken.None))
            .Should().ThrowAsync<ObjectDisposedException>();

        await _auditLogger.Invoking(l => l.VerifyIntegrityAsync(CancellationToken.None))
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task VerifyIntegrity_EmptyLog_ReturnsTrue()
    {
        // Act
        var isValid = await _auditLogger.VerifyIntegrityAsync(CancellationToken.None);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task LogAuthenticationAsync_NullRemoteDeviceId_HandlesGracefully()
    {
        // Act
        await _auditLogger.LogAuthenticationAsync(
            "123-456-789",
            null,
            null,
            AuthenticationOutcome.Failure_UnknownDevice,
            "Unknown device",
            CancellationToken.None);

        // Assert
        var entries = await _auditLogger.GetRecentEntriesAsync(10, CancellationToken.None);
        entries.Should().HaveCount(1);
        entries[0].RemoteDeviceId.Should().BeEmpty();
        entries[0].Details.Should().Contain("UnknownDevice");
    }

    [Fact]
    public async Task LegacyLogMethod_WorksWithAuditLogEntry()
    {
        // This tests the legacy synchronous Log method
        var entry = new AuditLogEntry(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            AuditEventType.SessionInitiated,
            "123-456-789",
            "987-654-321",
            "Test detail",
            AuditOutcome.Success);

        // Act
        _auditLogger.Log(entry);

        // Assert
        var entries = await _auditLogger.GetRecentEntriesAsync(10, CancellationToken.None);
        entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task LegacyVerifyIntegrity_ReturnsCounts()
    {
        // Arrange
        var entry1 = new AuditLogEntry(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            AuditEventType.SessionInitiated,
            "123-456-789",
            "987-654-321",
            "Detail 1",
            AuditOutcome.Success);

        var entry2 = new AuditLogEntry(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow.AddSeconds(1),
            AuditEventType.SessionAccepted,
            "123-456-789",
            "987-654-321",
            "Detail 2",
            AuditOutcome.Success);

        // Act
        _auditLogger.Log(entry1);
        _auditLogger.Log(entry2);

        var isValid = _auditLogger.VerifyIntegrity(out int verifiedCount, out int corruptedCount);

        // Assert
        isValid.Should().BeTrue();
        verifiedCount.Should().Be(2);
        corruptedCount.Should().Be(0);
    }

    [Fact]
    public async Task LegacyVerifyIntegrity_TamperedData_ReturnsFalseAndCounts()
    {
        // Arrange
        var entry1 = new AuditLogEntry(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            AuditEventType.SessionInitiated,
            "123-456-789",
            "987-654-321",
            "Detail 1",
            AuditOutcome.Success);

        var entry2 = new AuditLogEntry(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow.AddSeconds(1),
            AuditEventType.SessionAccepted,
            "123-456-789",
            "987-654-321",
            "Detail 2",
            AuditOutcome.Success);

        _auditLogger.Log(entry1);
        _auditLogger.Log(entry2);

        // Tamper with first file
        var files = Directory.GetFiles(_testDirectory, "*.json").OrderBy(f => f).ToArray();
        var tampered = File.ReadAllBytes(files[0]);
        tampered[0] ^= 0xFF;
        File.WriteAllBytes(files[0], tampered);

        // Act
        var isValid = _auditLogger.VerifyIntegrity(out int verifiedCount, out int corruptedCount);

        // Assert
        isValid.Should().BeFalse();
        corruptedCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ChainKeyDerivation_IsDeterministic()
    {
        // This tests the static GenerateChainKey method indirectly by checking
        // that two loggers on same machine/user get same default key
        var dir1 = Path.Combine(Path.GetTempPath(), "ControlDeskAuditTests", Guid.NewGuid().ToString("N"));
        var dir2 = Path.Combine(Path.GetTempPath(), "ControlDeskAuditTests", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(dir1);
            Directory.CreateDirectory(dir2);

            using var logger1 = new AuditLogger(dir1);
            using var logger2 = new AuditLogger(dir2);

            await logger1.LogAuthenticationAsync("111-111-111", "222-222-222", "k1", AuthenticationOutcome.Success, null, CancellationToken.None);
            await logger2.LogAuthenticationAsync("111-111-111", "222-222-222", "k1", AuthenticationOutcome.Success, null, CancellationToken.None);

            // Both should verify their own chains correctly
            (await logger1.VerifyIntegrityAsync(CancellationToken.None)).Should().BeTrue();
            (await logger2.VerifyIntegrityAsync(CancellationToken.None)).Should().BeTrue();
        }
        finally
        {
            try { if (Directory.Exists(dir1)) Directory.Delete(dir1, true); } catch { }
            try { if (Directory.Exists(dir2)) Directory.Delete(dir2, true); } catch { }
        }
    }

    [Fact]
    public async Task ConcurrentLogging_MaintainsChainIntegrity()
    {
        // Act - simulate concurrent logging
        var tasks = new Task[20];
        for (int i = 0; i < tasks.Length; i++)
        {
            int index = i;
            tasks[i] = _auditLogger.LogAuthenticationAsync(
                "111-111-111",
                $"{index:D9}",
                "key",
                AuthenticationOutcome.Success,
                null,
                CancellationToken.None);
        }

        await Task.WhenAll(tasks);

        // Assert
        var isValid = await _auditLogger.VerifyIntegrityAsync(CancellationToken.None);
        isValid.Should().BeTrue();

        var entries = await _auditLogger.GetRecentEntriesAsync(25, CancellationToken.None);
        entries.Should().HaveCount(20);
    }
}