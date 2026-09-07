using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AiPet.AI;

internal static class BoundedAiResponse
{
    public static async Task<string> ReadAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        if(content.Headers.ContentLength>maximumBytes) throw new JsonException("Response exceeds budget.");
        await using var input=await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output=new MemoryStream(); var buffer=new byte[8192];
        int read;
        while((read=await input.ReadAsync(buffer,cancellationToken).ConfigureAwait(false))>0)
        {
            if(output.Length+read>maximumBytes) throw new JsonException("Response exceeds budget.");
            output.Write(buffer,0,read);
        }
        try { return new UTF8Encoding(false,true).GetString(output.ToArray()); }
        catch(DecoderFallbackException ex) { throw new JsonException("Response encoding invalid.",ex); }
    }
}
