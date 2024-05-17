using ic4;
using System;

using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.Runtime.InteropServices;

namespace STI.Common
{
    public class Utils
    {

        public static void PrintDeviceList()
        {
            Console.WriteLine("Enumerating all attached video capture devices...");

            var deviceList = DeviceEnum.Devices;

            if (deviceList.Count == 0)
            {
                Console.WriteLine("No devices found");
            }

            Console.WriteLine($"Found {deviceList.Count} devices:");

            foreach (var deviceInfo in deviceList)
            {
                Console.WriteLine($"\t{FormatDeviceInfo(deviceInfo)}");
            }

            Console.WriteLine();
        }

        static string FormatDeviceInfo(ic4.DeviceInfo deviceInfo)
        {
            return $"Model: {deviceInfo.ModelName} Serial: {deviceInfo.Serial}";
        }

        // Clase de los productos
        public class Product
        {
            public int Id { get; set; }
            public int Code { get; set; }
            public string Name { get; set; }
            public double MaxD { get; set; }
            public double MinD { get; set; }
            public double MaxOvality { get; set; }
            public double MaxCompacity { get; set; }
            public int Grid { get; set; }
        }


        // Clase para representar el grid
        public class GridType
        {
            public int Type { get; set; }
            public (int, int) Grid { get; set; }
            public int[] QuadrantsOfInterest { get; set; }

            public GridType(int type, (int, int) grid, int[] quadrantsOfInterest)
            {
                Type = type;
                Grid = grid;
                QuadrantsOfInterest = quadrantsOfInterest;
            }
        }

        public class Blob
        {
            // Propiedades de la estructura Blob
            public double Area { get; set; }
            //public List<Point> AreaPoints { get; set; }
            public double Perimetro { get; set; }
            public VectorOfPoint PerimetroPoints { get; set; }
            public double DiametroIA { get; set; }
            public double Diametro { get; set; }
            public Point Centro { get; set; }
            public double DMayor { get; set; }
            public double DMenor { get; set; }
            public double Sector { get; set; }
            public double Compacidad { get; set; }
            public double Ovalidad { get; set; }
            public ushort Size { get; set; }
            //public double CorrectionFactor { get; set; }

            // Constructor de la clase Blob
            public Blob(double area, double perimetro, VectorOfPoint perimetroPoints, double diametro, double diametroIA, Point centro, double dMayor, double dMenor, double sector, double compacidad, ushort size, double ovalidad)
            {
                Area = area;
                //AreaPoints = areaPoints;
                Perimetro = perimetro;
                PerimetroPoints = perimetroPoints;
                Diametro = diametro;
                DiametroIA = diametroIA;
                Centro = centro;
                DMayor = dMayor;
                DMenor = dMenor;
                Sector = sector;
                Compacidad = compacidad;
                Size = size;
                Ovalidad = ovalidad;
                //CorrectionFactor = correctionFactor;
            }
        }

        // Clase para representar un cuadrante L_Q1
        public class Quadrant
        {
            public int Number { get; set; }
            public string ClassName { get; set; }
            public bool Found { get; set; }
            public double DiameterAvg { get; set; }
            public double DiameterMax { get; set; }
            public double DiameterMin { get; set; }
            public double Ratio { get; set; }
            public double Compacity { get; set; }

            public Blob Blob { get; set; }

            public Quadrant(int number, string className, bool found, double diameterAvg, double diameterMax, double diameterMin, double compacity, Blob blob)
            {
                // Inicializar las propiedades según sea necesario
                Number = number;
                ClassName = className;
                Found = found;
                DiameterAvg = diameterAvg;
                DiameterMax = diameterMax;
                DiameterMin = diameterMin;
                Ratio = diameterMax / diameterMin;
                Compacity = compacity;
                Blob = Blob;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
