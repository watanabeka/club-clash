using System.Collections.Generic;
using UnityEngine;

namespace ClubClash
{
    // The counter and fight callouts share a true 5×7 bitmap alphabet.
    public static class PixelDisplay
    {
        static readonly Dictionary<char,string[]> glyphs=new Dictionary<char,string[]>
        {
            {'0',Rows("01110/10001/10011/10101/11001/10001/01110")},
            {'1',Rows("00100/01100/00100/00100/00100/00100/01110")},
            {'2',Rows("01110/10001/00001/00010/00100/01000/11111")},
            {'3',Rows("11110/00001/00001/01110/00001/00001/11110")},
            {'4',Rows("00010/00110/01010/10010/11111/00010/00010")},
            {'5',Rows("11111/10000/10000/11110/00001/00001/11110")},
            {'6',Rows("01110/10000/10000/11110/10001/10001/01110")},
            {'7',Rows("11111/00001/00010/00100/01000/01000/01000")},
            {'8',Rows("01110/10001/10001/01110/10001/10001/01110")},
            {'9',Rows("01110/10001/10001/01111/00001/00001/01110")},
            {'F',Rows("11111/10000/10000/11110/10000/10000/10000")},
            {'I',Rows("11111/00100/00100/00100/00100/00100/11111")},
            {'G',Rows("01110/10001/10000/10111/10001/10001/01110")},
            {'H',Rows("10001/10001/10001/11111/10001/10001/10001")},
            {'T',Rows("11111/00100/00100/00100/00100/00100/00100")},
            {'D',Rows("11110/10001/10001/10001/10001/10001/11110")},
            {'R',Rows("11110/10001/10001/11110/10100/10010/10001")},
            {'A',Rows("01110/10001/10001/11111/10001/10001/10001")},
            {'W',Rows("10001/10001/10001/10101/10101/10101/01010")},
            {'K',Rows("10001/10010/10100/11000/10100/10010/10001")},
            {'O',Rows("01110/10001/10001/10001/10001/10001/01110")},
            {'M',Rows("10001/11011/10101/10101/10001/10001/10001")},
            {'E',Rows("11111/10000/10000/11110/10000/10000/11111")},
            {'V',Rows("10001/10001/10001/10001/10001/01010/00100")},
            {'S',Rows("01111/10000/10000/01110/00001/00001/11110")},
            {'.',Rows("00000/00000/00000/00000/00000/00100/00100")},
            {' ',Rows("00000/00000/00000/00000/00000/00000/00000")}
        };
        static string[] Rows(string value){return value.Split('/');}
        public static void Draw(Rect box,string value,Color color,float pixelSize)
        {
            if(string.IsNullOrEmpty(value))return;
            float columns=value.Length*6-1,pixel=Mathf.Min(pixelSize,box.width/columns,box.height/7);
            float x=box.center.x-columns*pixel*.5f,y=box.center.y-7*pixel*.5f;Color previous=GUI.color;GUI.color=color;
            for(int i=0;i<value.Length;i++)
            {
                string[] rows;if(!glyphs.TryGetValue(char.ToUpperInvariant(value[i]),out rows))continue;
                for(int row=0;row<7;row++)for(int column=0;column<5;column++)if(rows[row][column]=='1')GUI.DrawTexture(new Rect(x+(i*6+column)*pixel,y+row*pixel,pixel,pixel),Texture2D.whiteTexture);
            }
            GUI.color=previous;
        }
    }
}
