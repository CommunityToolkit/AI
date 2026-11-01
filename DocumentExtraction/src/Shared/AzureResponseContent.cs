// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure;

namespace CommunityToolkit.DocumentExtraction;

internal static class AzureResponseContent
{
    public static byte[]? GetBufferedResponseBytes(Response response)
    {
        Stream? stream = response.ContentStream;
        if (stream is null || !stream.CanSeek || !stream.CanRead)
        {
            return null;
        }

        long originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}
