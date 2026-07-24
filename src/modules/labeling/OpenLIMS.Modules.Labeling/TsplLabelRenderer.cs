using System.Text;
using OpenLIMS.Contracts.Labeling;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Labeling;

internal static class TsplLabelRenderer
{
    public static byte[] Render(ReceivingLabelObjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var objectTitle = snapshot.ObjectType == ReceivingLabelObjectTypes.Container ? "包装 CONTAINER" : "实物 ITEM";
        var payload = LabelBarcodeCodec.Create(snapshot.ObjectType, Guid.Parse(snapshot.OpaqueReference));
        var template = LabelTemplateVersions.ForObjectType(snapshot.ObjectType);
        var command = $"""
            SIZE 50 mm,30 mm
            GAP 2 mm,0 mm
            DIRECTION 1
            REFERENCE 0,0
            CODEPAGE UTF-8
            CLS
            QRCODE 18,18,L,5,A,0,M2,S7,"{payload}"
            TEXT 180,18,"TSS24.BF2",0,1,1,"{objectTitle}"
            TEXT 180,58,"3",0,1,1,"{snapshot.BusinessNumber}"
            TEXT 180,92,"3",0,1,1,"LAB:{snapshot.LaboratoryCode}"
            TEXT 180,126,"2",0,1,1,"{template}"
            PRINT 1,1
            """;
        return Encoding.UTF8.GetBytes(command.ReplaceLineEndings("\r\n"));
    }
}
