using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace AndroidMultiDeviceV1.Core;

/// <summary>
/// Read an ordinary .xlsx account workbook without NuGet dependencies. First worksheet,
/// columns username/email/password by header (any order) or A/B/C if headerless.
/// Does not evaluate formulas and never logs input values/passwords.
/// </summary>
public static class ExcelAccountImporter
{
    static readonly XNamespace Ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static readonly XNamespace OfficeRel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    const long MaxUncompressedPart=32L*1024*1024;
    const int MaxRows=100_000;

    public static List<string> ReadLines(string path)
    {
        using var archive=ZipFile.OpenRead(path);
        var strings=ReadSharedStrings(archive);
        var entry=ResolveFirstWorksheet(archive);
        var doc=ReadDocument(entry);
        var sheetData=doc.Root?.Element(Ns+"sheetData") ??
            throw new InvalidDataException("Excel không có vùng dữ liệu hợp lệ.");
        var result=new List<string>();
        int[] columns=[0,1,2]; // username, email, password
        bool firstNonEmpty=true;
        int count=0;
        foreach(var row in sheetData.Elements(Ns+"row"))
        {
            if(++count>MaxRows)throw new InvalidDataException("Excel vượt 100.000 dòng; hãy chia nhỏ file.");
            var cells=new Dictionary<int,string>();
            foreach(var cell in row.Elements(Ns+"c"))
            {
                string address=(string?)cell.Attribute("r") ?? "";
                int column=ColumnIndex(address);
                if(column<0 || column>255)continue;
                cells[column]=CellValue(cell,strings);
            }
            if(cells.Values.All(string.IsNullOrWhiteSpace))continue;
            // Also accept an Excel sheet with one column containing full
            // username|email|password records, one record per row.
            var populated=cells.Values.Where(v=>!string.IsNullOrWhiteSpace(v)).ToList();
            if(populated.Count==1 && populated[0].Count(ch=>ch=='|')>=2)
            {
                firstNonEmpty=false;
                if(populated[0].Trim().Equals("username|email|password",StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(populated[0]);
                continue;
            }
            if(firstNonEmpty)
            {
                firstNonEmpty=false;
                var recognized=new Dictionary<string,int>();
                foreach(var kv in cells)
                {
                    string key=NormalizeHeader(kv.Value);
                    string? name=key switch {
                        "username" or "usernametiktok" or "tiktokusername" or "tiktokhandle" or "handle" or "tentiktok" or "tentk"=>"username",
                        "email" or "emailgoogle" or "googleemail" or "gmail" or "mail"=>"email",
                        "password" or "passwordgoogle" or "googlepassword" or "pass" or "passgg" or "matkhau" or "matkhaugoogle"=>"password",
                        _=>null
                    };
                    if(name is not null && !recognized.ContainsKey(name))recognized[name]=kv.Key;
                }
                if(recognized.Count==3)
                {
                    columns=[recognized["username"],recognized["email"],recognized["password"]];
                    continue; // Skip header
                }
                if(recognized.Count>0)
                    throw new InvalidDataException("Excel có tiêu đề không đầy đủ: cần Username, Email, Password.");
            }
            string username=Get(cells,columns[0]);
            string email=Get(cells,columns[1]);
            string password=GetRaw(cells,columns[2]);
            // Rows with Excel formulas are rejected at vault validation, not calculated.
            result.Add(username+"|"+email+"|"+password);
        }
        return result;
    }

    static string Get(Dictionary<int,string> cells,int col)=>GetRaw(cells,col).Trim();
    static string GetRaw(Dictionary<int,string> cells,int col)=>cells.GetValueOrDefault(col,"");

    static string NormalizeHeader(string s)
    {
        string normalized=s.Trim().Normalize(NormalizationForm.FormD);
        var sb=new StringBuilder();
        foreach(char c in normalized)
        {
            if(CharUnicodeInfo.GetUnicodeCategory(c)==UnicodeCategory.NonSpacingMark)continue;
            if(char.IsLetterOrDigit(c))sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString().Replace('đ','d');
    }

    static int ColumnIndex(string reference)
    {
        int col=0,length=0;
        foreach(char c in reference)
        {
            if(c is >= 'A' and <= 'Z') { col=col*26+(c-'A'+1);length++; }
            else if(c is >= 'a' and <= 'z') { col=col*26+(c-'a'+1);length++; }
            else break;
        }
        return length==0?-1:col-1;
    }

    static string CellValue(XElement cell,List<string> shared)
    {
        // Avoid trusting stale formula caches in credentials and avoid evaluating code.
        if(cell.Element(Ns+"f") is not null || (string?)cell.Attribute("t")=="e")return "";
        string type=(string?)cell.Attribute("t") ?? "";
        if(type=="inlineStr")return string.Concat(cell.Descendants(Ns+"t").Select(t=>t.Value));
        string value=(string?)cell.Element(Ns+"v") ?? "";
        if(type=="s")
            return int.TryParse(value,out int index) && index>=0 && index<shared.Count
                ? shared[index] : "";
        return value;
    }

    static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry=archive.GetEntry("xl/sharedStrings.xml");
        if(entry is null)return [];
        var doc=ReadDocument(entry);
        return doc.Root?.Elements(Ns+"si")
            .Select(si=>string.Concat(si.Descendants(Ns+"t").Select(x=>x.Value))).ToList() ?? [];
    }

    static ZipArchiveEntry ResolveFirstWorksheet(ZipArchive archive)
    {
        var workbook=archive.GetEntry("xl/workbook.xml");
        var relationships=archive.GetEntry("xl/_rels/workbook.xml.rels");
        if(workbook is not null && relationships is not null)
        {
            var book=ReadDocument(workbook);
            string? id=(string?)book.Root?.Element(Ns+"sheets")?.Elements(Ns+"sheet")
                .FirstOrDefault()?.Attribute(OfficeRel+"id");
            var rels=ReadDocument(relationships);
            string? target=(string?)rels.Root?.Elements()
                .FirstOrDefault(r=>(string?)r.Attribute("Id")==id)?.Attribute("Target");
            if(!string.IsNullOrWhiteSpace(target))
            {
                var baseUri=new Uri("https://xlsx.invalid/xl/workbook.xml");
                string entryName=Uri.UnescapeDataString(new Uri(baseUri,target).AbsolutePath).TrimStart('/');
                var match=archive.GetEntry(entryName);
                if(match is not null && entryName.StartsWith("xl/worksheets/",StringComparison.OrdinalIgnoreCase))
                    return match;
            }
        }
        return archive.GetEntry("xl/worksheets/sheet1.xml") ??
            throw new InvalidDataException("Không tìm thấy sheet đầu tiên của file Excel .xlsx.");
    }

    static XDocument ReadDocument(ZipArchiveEntry entry)
    {
        if(entry.Length>MaxUncompressedPart)
            throw new InvalidDataException("Một phần dữ liệu Excel quá lớn (giới hạn 32MB).");
        using var stream=entry.Open();
        using var reader=XmlReader.Create(stream,new XmlReaderSettings {
            DtdProcessing=DtdProcessing.Prohibit,
            XmlResolver=null,
            MaxCharactersInDocument=MaxUncompressedPart,
            IgnoreComments=true
        });
        return XDocument.Load(reader);
    }
}
