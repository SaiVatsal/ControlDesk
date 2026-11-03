using System;
using System.Threading;
using System.Threading.Tasks;

namespace ControlDesk.Security.Authentication;

/// <summary>
/// Interface for performing mutual challenge-response authentication using Ed25519 signatures.
/// </summary>
public interface IChallengeResponseAuthenticator
{
    /// <summary>
    /// Generates a cryptographically random challenge nonce for authentication.
    /// </summary>
    /// <param name="length">Length of the challenge in bytes (default 32).</param>
    /// <returns>Random challenge bytes.</returns>
    byte[] GenerateChallenge(int length = 32);

    /// <summary>
    /// Creates a challenge response by signing the challenge with the local device's private key.
    /// </summary>
    /// <param name="challenge">The challenge bytes received from the remote party.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The signature (64 bytes for Ed25519).</returns>
    Task<byte[]> CreateResponseAsync(byte[] challenge, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a challenge response from a remote device against their trusted public key.
    /// </summary>
    /// <param name="challenge">The original challenge sent to the remote party.</param>
    /// <param name="response">The signature received from the remote party.</param>
    /// <param name="remotePublicKeyBase64">The remote device's Ed25519 public key in Base64.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the signature is valid for the challenge.</returns>
    Task<bool> VerifyResponseAsync(byte[] challenge, byte[] response, string remotePublicKeyBase64, CancellationToken cancellationToken = default);
}