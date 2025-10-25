using System.Text;
using CadDrawPic.Services;

namespace CadDrawPic;

internal class Program
{
    static void Main(string[] args)
    {
        // 注册编码提供程序以支持GBK编码
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var processor = new CADProcessor();
        processor.ProcessCADData();

        Console.ReadKey();
    }
}