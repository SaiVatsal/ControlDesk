using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ControlDesk.Security.Authentication;
using ControlDesk.Security.Cryptography;
using ControlDesk.Security.Storage;
using ControlDesk.Shared.Protocol;
using FluentAssertions;
using Xunit;

namespace ControlDesk.Security.Tests;

public sealed class ChallengeResponseAuthenticatorTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly WindowsDpapiCredentialStore _credentialStore;
    private readonly Ed25519KeyPair _keyPair;
    private readonly ChallengeResponseAuthenticator _authenticator;

    public ChallengeResponseAuthenticatorTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "ControlDeskAuthTests", Guid.NewGuid().ToString("N"));
        _credentialStore = new WindowsDpapiCredentialStore(_testDirectory);
        _keyPair = new Ed25519KeyPair();
        _authenticator = new ChallengeResponseAuthenticator(_credentialStore, _keyPair);
    }

    public void Dispose()
    {
        _authenticator.Dispose();
        _keyPair.Dispose();
        _credentialStore.Dispose();
        try
        {
            if (Directory.Exists(_testDirectory))
                Directory.Delete(_testDirectory, true);
        }
        catch { }
    }

    [Fact]
    public void GenerateChallenge_ReturnsRandomBytesOfRequestedLength()
    {
        // Act
        var challenge1 = _authenticator.GenerateChallenge(32);
        var challenge2 = _authenticator.GenerateChallenge(32);

        // Assert
        challenge1.Should().HaveCount(32);
        challenge2.Should().HaveCount(32);
        challenge1.Should().NotEqual(challenge2);
    }

    [Fact]
    public async Task CreateResponseAsync_SignsChallengeWithPrivateKey()
    {
        // Arrange
        var challenge = _authenticator.GenerateChallenge(32);

        // Act
        var response = await _authenticator.CreateResponseAsync(challenge);

        // Assert
        response.Should().HaveCount(64);
        _keyPair.Verify(challenge, response).Should().BeTrue();
    }

    [Fact]
    public async Task CreateResponseAsync_EmptyChallenge_ThrowsArgumentException()
    {
        // Act & Assert
        await _authenticator.Invoking(a => a.CreateResponseAsync(Array.Empty<byte>()))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task VerifyResponseAsync_ValidSignature_ReturnsTrue()
    {
        // Arrange
        var challenge = _authenticator.GenerateChallenge(32);
        var response = await _authenticator.CreateResponseAsync(challenge);

        // Act
        var isValid = await _authenticator.VerifyResponseAsync(challenge, response, _keyPair.PublicKeyBase64);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyResponseAsync_InvalidSignature_ReturnsFalse()
    {
        // Arrange
        var challenge = _authenticator.GenerateChallenge(32);
        var response = await _authenticator.CreateResponseAsync(challenge);
        response[0] ^= 0xFF; // Corrupt signature

        // Act
        var isValid = await _authenticator.VerifyResponseAsync(challenge, response, _keyPair.PublicKeyBase64);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyResponseAsync_WrongPublicKey_ReturnsFalse()
    {
        // Arrange
        using var otherKey = new Ed25519KeyPair();
        var challenge = _authenticator.GenerateChallenge(32);
        var response = await _authenticator.CreateResponseAsync(challenge);

        // Act
        var isValid = await _authenticator.VerifyResponseAsync(challenge, response, otherKey.PublicKeyBase64);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void LegacyChallengeFlow_ProcessAndVerify_Succeeds()
    {
        // Arrange
        var controllerId = "111-222-333";
        var targetId = "999-888-777";
        _credentialStore.StoreTrustedDevice(controllerId, _keyPair.PublicKeyBase64, "ControllerPC");

        var request = _authenticator.CreateChallengeRequest(targetId, controllerId);

        // Host processes request
        var response = _authenticator.ProcessChallengeRequest(request);
        response.NonceBase64.Should().NotBeNullOrEmpty();

        // Controller creates verification
        var verification = _authenticator.CreateChallengeVerification(response, "dummy_sdp");
        verification.SignatureBase64.Should().NotBeNullOrEmpty();

        // Host verifies
        var verified = _authenticator.VerifyChallengeResponse(response, verification, controllerId, out var verifiedPubKey);

        // Assert
        verified.Should().BeTrue();
        verifiedPubKey.Should().Be(_keyPair.PublicKeyBase64);
    }
}