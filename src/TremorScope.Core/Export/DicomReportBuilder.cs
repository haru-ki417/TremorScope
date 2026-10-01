using System.Globalization;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;
using TremorScope.Core.Domain;

namespace TremorScope.Core.Export;

/// <summary>PACS に送る ID の種類</summary>
public enum PacsPatientIdMode
{
    /// <summary>院内のカルテ番号（院内の PACS で、ほかの検査と同じ患者にまとめたいとき）</summary>
    LocalId = 0,

    /// <summary>仮名 ID（院外や研究用の PACS に送るとき）</summary>
    Pseudonym = 1,
}

/// <summary>
/// 結果のレポート画像を DICOM（Secondary Capture: 他の機器で作った画像）にする。
/// 画面を撮影するのではなく、決まった大きさで描いたレポート画像を受け取る。
/// </summary>
public static class DicomReportBuilder
{
    public const string Manufacturer = "TremorScope";

    public static DicomFile Build(byte[] rgbPixels, int width, int height, SessionSummary session, PacsPatientIdMode idMode, string softwareVersion)
    {
        ArgumentNullException.ThrowIfNull(rgbPixels);
        ArgumentNullException.ThrowIfNull(session);
        if (width <= 0 || height <= 0 || rgbPixels.Length != width * height * 3)
            throw new ArgumentException("画像の大きさと画素の数が合いません。", nameof(rgbPixels));

        var local = session.MeasuredAtUtc.ToLocalTime();
        string patientId = idMode == PacsPatientIdMode.LocalId ? session.LocalId : session.PseudonymId;

        var ds = new DicomDataset
        {
            { DicomTag.SpecificCharacterSet, "ISO_IR 192" },
            { DicomTag.SOPClassUID, DicomUID.SecondaryCaptureImageStorage },
            { DicomTag.SOPInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID() },
            // 同じ測定（受診）から作った画像は同じ検査にまとめる
            { DicomTag.StudyInstanceUID, UidFromGuid(session.Id) },
            { DicomTag.SeriesInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID() },
            { DicomTag.PatientID, patientId },
            { DicomTag.PatientName, idMode == PacsPatientIdMode.Pseudonym ? session.PseudonymId : "" },
            { DicomTag.PatientBirthDate, "" },
            { DicomTag.PatientSex, "" },
            { DicomTag.StudyDate, local.ToString("yyyyMMdd", CultureInfo.InvariantCulture) },
            { DicomTag.StudyTime, local.ToString("HHmmss", CultureInfo.InvariantCulture) },
            { DicomTag.ContentDate, local.ToString("yyyyMMdd", CultureInfo.InvariantCulture) },
            { DicomTag.ContentTime, local.ToString("HHmmss", CultureInfo.InvariantCulture) },
            { DicomTag.AccessionNumber, "" },
            { DicomTag.ReferringPhysicianName, "" },
            { DicomTag.StudyID, "" },
            { DicomTag.StudyDescription, "Tremor accelerometry" },
            { DicomTag.SeriesDescription, "TremorScope report" },
            { DicomTag.Modality, "OT" },
            { DicomTag.ConversionType, "WSD" },
            { DicomTag.SeriesNumber, "900" },
            { DicomTag.InstanceNumber, "1" },
            { DicomTag.Manufacturer, Manufacturer },
            { DicomTag.SoftwareVersions, softwareVersion },
            { DicomTag.BurnedInAnnotation, "YES" },
            { DicomTag.ImageComments, Summary(session) },
            { DicomTag.PhotometricInterpretation, PhotometricInterpretation.Rgb.Value },
            { DicomTag.Rows, (ushort)height },
            { DicomTag.Columns, (ushort)width },
            { DicomTag.BitsAllocated, (ushort)8 },
            { DicomTag.BitsStored, (ushort)8 },
            { DicomTag.HighBit, (ushort)7 },
            { DicomTag.PixelRepresentation, (ushort)0 },
            { DicomTag.SamplesPerPixel, (ushort)3 },
            { DicomTag.PlanarConfiguration, (ushort)0 },
        };

        if (idMode == PacsPatientIdMode.Pseudonym)
        {
            ds.Add(DicomTag.PatientIdentityRemoved, "YES");
            ds.Add(DicomTag.DeidentificationMethod, "TremorScope pseudonymization (HMAC-SHA256, site key)");
        }

        var pixelData = DicomPixelData.Create(ds, true);
        pixelData.AddFrame(new MemoryByteBuffer(rgbPixels));
        return new DicomFile(ds);
    }

    /// <summary>画像に焼き込んだ内容を、検索できる文字としても残す（英数字のみ）</summary>
    internal static string Summary(SessionSummary s)
    {
        static string Part(string label, Analysis.TremorMetrics? m) => m is null
            ? $"{label}: n/a"
            : string.Create(CultureInfo.InvariantCulture, $"{label}: {m.PeakFrequencyHz:0.0} Hz, {m.RmsAccelerationMg:0.0} mg RMS, quality {m.Quality.Level}");
        return $"{Part("Rest", s.Rest)}; {Part("Postural", s.Postural)}; pattern {s.Comparison.Pattern}; hand {s.Hand}. Measurement aid, not a diagnosis.";
    }

    /// <summary>GUID から DICOM の UID（2.25.＋整数）を作る</summary>
    internal static string UidFromGuid(Guid id)
    {
        var bytes = id.ToByteArray();
        Array.Resize(ref bytes, 17); // 先頭ビットが立っていても正の数として扱う
        return "2.25." + new System.Numerics.BigInteger(bytes).ToString(CultureInfo.InvariantCulture);
    }
}
