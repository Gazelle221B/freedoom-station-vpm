using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class DoomInputAck : UdonSharpBehaviour
{
    public Texture2D Buffer;
    public DoomControl Control;

    private const int STATE_WIDTH = 64;
    private const float MULT = 255.0f;
    private const float ADD = 0.5f;

    private uint DecodePackedData(int x, int y, int c)
    {
        Color col0 = Buffer.GetPixel(x, y);
        Color col1 = Buffer.GetPixel(x + STATE_WIDTH, y);
        Color col2 = Buffer.GetPixel(x + STATE_WIDTH * 2, y);
        Color col3 = Buffer.GetPixel(x + STATE_WIDTH * 3, y);
        Color col4 = Buffer.GetPixel(x + STATE_WIDTH * 4, y);
        Color col5 = Buffer.GetPixel(x + STATE_WIDTH * 5, y);

        switch (c)
        {
            case 0:
                return (uint)(col0.r * MULT + ADD)
                    | ((uint)(col1.r * MULT + ADD) << 8)
                    | ((uint)(col2.r * MULT + ADD) << 16)
                    | ((uint)(col3.r * MULT + ADD) << 24);
            case 1:
                return (uint)(col0.g * MULT + ADD)
                    | ((uint)(col1.g * MULT + ADD) << 8)
                    | ((uint)(col2.g * MULT + ADD) << 16)
                    | ((uint)(col3.g * MULT + ADD) << 24);
            case 2:
                return (uint)(col0.b * MULT + ADD)
                    | ((uint)(col1.b * MULT + ADD) << 8)
                    | ((uint)(col2.b * MULT + ADD) << 16)
                    | ((uint)(col3.b * MULT + ADD) << 24);
            case 3:
                return (uint)(col4.r * MULT + ADD)
                    | ((uint)(col4.g * MULT + ADD) << 8)
                    | ((uint)(col4.b * MULT + ADD) << 16)
                    | ((uint)(col5.r * MULT + ADD) << 24);
        }
        return 0;
    }

    private uint LoadUartInputTag()
    {
        return DecodePackedData(9, 0, 3);
    }

    public void OnPostRender()
    {
        if (Buffer == null || Control == null) return;
        Buffer.ReadPixels(new Rect(0, 0, 384, 64), 0, 0);
        Control.UartTagAck = (int)LoadUartInputTag();
    }
}
