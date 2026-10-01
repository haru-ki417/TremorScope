using TremorScope.Core.Analysis;
using TremorScope.Core.Domain;
using TremorScope.Core.Export;
using TremorScope.Core.Privacy;
using TremorScope.Core.Sensors;
using FellowOakDicom;

namespace TremorScope.Tests;

public class PrivacyAndExportTests
{
    private static readonly byte[] KeyA = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] KeyB = Enumerable.Range(2, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void 同じ施設_同じカルテ番号なら同じ仮名IDになる()
    {
        var p = new Pseudonymizer(KeyA);
        Assert.Equal(p.Pseudonymize("12345"), p.Pseudonymize(" １２３４５ ")); // 全角・空白の違いは同じとみなす
        Assert.Matches("^TS-[A-Z2-9]{10}$", p.Pseudonymize("12345"));
    }

    [Fact]
    public void 別の施設や別の患者では仮名IDが変わり_カルテ番号を含まない()
    {
        var a = new Pseudonymizer(KeyA).Pseudonymize("12345");
        Assert.NotEqual(a, new Pseudonymizer(KeyB).Pseudonymize("12345"));
        Assert.NotEqual(a, new Pseudonymizer(KeyA).Pseudonymize("12346"));
        Assert.DoesNotContain("12345", a, StringComparison.Ordinal);
    }

    private static SessionSummary Session()
    {
        var rest = TremorAnalyzer.Analyze(SimulatedSource.CreateRecording(SimulationProfile.RestTremor, Condition.Rest, 20)).Metrics;
        var postural = TremorAnalyzer.Analyze(SimulatedSource.CreateRecording(SimulationProfile.RestTremor, Condition.Postural, 20)).Metrics;
        return new SessionSummary(Guid.NewGuid(), Guid.NewGuid(), "=CMD|12345", "TS-ABCDEFGHJK", new DateTime(2026, 10, 2, 1, 0, 0, DateTimeKind.Utc),
            Hand.Right, rest, postural, ConditionComparer.Compare(rest, postural), null);
    }

    [Fact]
    public void 院外向けのCSVにはカルテ番号を入れない()
    {
        var s = Session();
        string external = CsvExporter.Sessions([s], includeLocalId: false);
        Assert.DoesNotContain("12345", external, StringComparison.Ordinal);
        Assert.Contains("TS-ABCDEFGHJK", external, StringComparison.Ordinal);

        string internalCsv = CsvExporter.Sessions([s], includeLocalId: true);
        Assert.Contains("'=CMD|12345", internalCsv, StringComparison.Ordinal); // 数式として実行されないようにする
    }

    [Fact]
    public void DICOMは選んだIDで作られ_仮名のときは匿名化の印が付く()
    {
        var s = Session();
        var pixels = new byte[40 * 30 * 3];

        var pseudo = DicomReportBuilder.Build(pixels, 40, 30, s, PacsPatientIdMode.Pseudonym, "1.0.0").Dataset;
        Assert.Equal("TS-ABCDEFGHJK", pseudo.GetString(DicomTag.PatientID));
        Assert.Equal("YES", pseudo.GetString(DicomTag.PatientIdentityRemoved));
        Assert.Equal(DicomUID.SecondaryCaptureImageStorage, pseudo.GetSingleValue<DicomUID>(DicomTag.SOPClassUID));
        Assert.Contains("not a diagnosis", pseudo.GetString(DicomTag.ImageComments), StringComparison.Ordinal);

        var local = DicomReportBuilder.Build(pixels, 40, 30, s, PacsPatientIdMode.LocalId, "1.0.0").Dataset;
        Assert.Equal("=CMD|12345", local.GetString(DicomTag.PatientID));
        Assert.False(local.Contains(DicomTag.PatientIdentityRemoved));

        // 同じ測定から作った画像は同じ検査（Study）にまとまる
        Assert.Equal(pseudo.GetString(DicomTag.StudyInstanceUID), local.GetString(DicomTag.StudyInstanceUID));
        Assert.StartsWith("2.25.", pseudo.GetString(DicomTag.StudyInstanceUID), StringComparison.Ordinal);
    }

    [Fact]
    public void 画素の数が合わなければDICOMを作らない() =>
        Assert.Throws<ArgumentException>(() => DicomReportBuilder.Build(new byte[10], 40, 30, Session(), PacsPatientIdMode.LocalId, "1.0.0"));
}
