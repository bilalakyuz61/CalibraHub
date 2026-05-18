using CalibraHub.Application.Contracts.Common;

namespace CalibraHub.Application.UnitTests.Contracts;

/// <summary>
/// ApiResponse&lt;T&gt; + ApiResponse + ApiError factory metotlarinin
/// dogru cevap yapilarini urettigini garanti eder. Rapor §2.7 — JSON endpoint'lerinin
/// standart cevap formati. Sozlesme stabil kalmali.
/// </summary>
public sealed class ApiResponseTests
{
    [Fact]
    public void Successful_WithData_SetsSuccessTrueAndPopulatesData()
    {
        var result = ApiResponse<string>.Successful("hello");

        result.Success.Should().BeTrue();
        result.Data.Should().Be("hello");
        result.Error.Should().BeNull();
        result.Warnings.Should().BeNull();
    }

    [Fact]
    public void Successful_WithWarnings_PopulatesWarnings()
    {
        var warnings = new[] { "Uyari 1", "Uyari 2" };
        var result = ApiResponse<int>.Successful(42, warnings);

        result.Success.Should().BeTrue();
        result.Data.Should().Be(42);
        result.Warnings.Should().BeEquivalentTo(warnings);
    }

    [Fact]
    public void Failed_WithMessage_SetsSuccessFalseAndPopulatesError()
    {
        var result = ApiResponse<string>.Failed("Bir hata olustu");

        result.Success.Should().BeFalse();
        result.Data.Should().BeNull();
        result.Error.Should().NotBeNull();
        result.Error!.Message.Should().Be("Bir hata olustu");
        result.Error.TraceId.Should().BeNull();
    }

    [Fact]
    public void Failed_WithMessageAndTraceId_PopulatesBoth()
    {
        var result = ApiResponse<string>.Failed("msg", traceId: "abc-123", detail: "stack");

        result.Error!.TraceId.Should().Be("abc-123");
        result.Error.Detail.Should().Be("stack");
    }

    [Fact]
    public void Failed_WithApiError_PassesThrough()
    {
        var errors = new Dictionary<string, string[]> { ["Email"] = new[] { "Gecersiz" } };
        var apiError = new ApiError("Form hatasi", "tid", null, errors);
        var result = ApiResponse<string>.Failed(apiError);

        result.Error.Should().BeSameAs(apiError);
        result.Error!.Errors.Should().NotBeNull();
        result.Error.Errors!["Email"].Should().ContainSingle().Which.Should().Be("Gecersiz");
    }

    [Fact]
    public void NonGeneric_Successful_SetsSuccessTrueDataNull()
    {
        var result = ApiResponse.Successful();

        result.Success.Should().BeTrue();
        result.Data.Should().BeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void NonGeneric_Failed_WithMessage_SetsErrorPopulated()
    {
        var result = ApiResponse.Failed("hata");

        result.Success.Should().BeFalse();
        result.Error!.Message.Should().Be("hata");
    }

    [Fact]
    public void ApiError_RecordEquality_WorksAsExpected()
    {
        var a = new ApiError("msg", "tid");
        var b = new ApiError("msg", "tid");
        a.Should().Be(b);    // record value equality
    }
}
