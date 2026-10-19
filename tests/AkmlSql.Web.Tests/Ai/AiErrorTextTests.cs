using System.Net;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

/// <summary>
/// The provider error the AI panel shows once the response body is dropped (review WEB-21). Google rejects a bad key with
/// HTTP 400 and the reason API_KEY_INVALID; the message must still say the key was rejected, not "refused the request (HTTP 400, 400)".
/// </summary>
public class AiErrorTextTests
{
    private const string GoogleBadKey =
        "{\"error\":{\"code\":400,\"message\":\"API key not valid. Please pass a valid API key.\",\"status\":\"INVALID_ARGUMENT\"," +
        "\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.ErrorInfo\",\"reason\":\"API_KEY_INVALID\",\"domain\":\"googleapis.com\"}]}}";

    [Fact]
    public void A_key_Google_rejects_with_HTTP_400_says_the_key_was_rejected()
    {
        var code = AiErrorText.ExtractCode(GoogleBadKey);

        Assert.Equal("API_KEY_INVALID", code);
        Assert.Equal("gemini rejected the API key. Check the key in Settings › AI (HTTP 400, API_KEY_INVALID).",
            AiErrorText.ForStatus("gemini", HttpStatusCode.BadRequest, code));
    }

    [Fact]
    public void A_numeric_code_that_repeats_the_status_gives_way_to_a_named_one()
    {
        Assert.Equal("INVALID_ARGUMENT", AiErrorText.ExtractCode("{\"error\":{\"code\":400,\"status\":\"INVALID_ARGUMENT\"}}"));
        Assert.Equal("1234", AiErrorText.ExtractCode("{\"error\":{\"code\":1234}}"));   // a number alone is still kept
    }

    [Fact]
    public void Other_providers_codes_are_read_as_before()
    {
        Assert.Equal("invalid_api_key", AiErrorText.ExtractCode("{\"error\":{\"message\":\"x\",\"type\":\"invalid_request_error\",\"code\":\"invalid_api_key\"}}"));
        Assert.Equal("authentication_error", AiErrorText.ExtractCode("{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"x\"}}"));
        Assert.Equal("openai refused the request (HTTP 400, bad_input).", AiErrorText.ForStatus("openai", HttpStatusCode.BadRequest, "bad_input"));
    }
}
