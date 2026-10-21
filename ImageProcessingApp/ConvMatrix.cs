using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageProcessingApp
{
    public class ConvMatrix
    {

        public int TopLeft = 0, TopMid = 0, TopRight = 0;              // 0 0 0
        public int MidLeft = 0, Pixel = 1, MidRight = 0;               // 0 1 0
        public int BottomLeft = 0, BottomMid = 0, BottomRight = 0;     // 0 0 0
        public int Factor = 1;
        public int Offset = 0;

        public void SetAll(int nVal)
        {
            TopLeft = TopMid = TopRight = MidLeft = Pixel = MidRight = BottomLeft = BottomMid = BottomRight = nVal;
        }
    }
}
