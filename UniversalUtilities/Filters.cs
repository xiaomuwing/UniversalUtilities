using System;

namespace UniversalUtilities
{
    /// <summary>
    /// 滤波操作
    /// </summary>
    public static class Filters
    {
        /// <summary>
        /// 平均数滤波
        /// </summary>
        /// <param name="data"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static double[] MeanFilter(double[] data, int size)
        {
            double[] result = new double[data.Length];
            if (size % 2 != 0)
            {
                size += 1;
            }
            int halfSize = size / 2;
            Array.Copy(data, 0, result, 0, halfSize);
            for (int i = halfSize; i < data.Length - halfSize; i++)
            {
                double sum = 0;
                for (int j = -halfSize; j < halfSize; j++)
                {
                    sum += data[i + j];
                }
                int count = size;
                result[i] = sum / count;
            }
            Array.Copy(data, data.Length - halfSize, result, data.Length - halfSize, halfSize);
            return result;
        }
        /// <summary>
        /// 中位数滤波
        /// </summary>
        /// <param name="data"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static double[] MedianFilter(double[] data, int size)
        {
            double[] result = new double[data.Length];
            int halfSize = size / 2;
            Array.Copy(data, 0, result, 0, halfSize);
            for (int i = halfSize; i < data.Length - halfSize; i++)
            {
                double[] window = new double[size];
                for (int j = -halfSize; j < halfSize; j++)
                {
                    window[j + halfSize] = data[i + j];
                }
                Array.Sort(window);
                result[i] = window[halfSize];
            }
            Array.Copy(data, data.Length - halfSize, result, data.Length - halfSize, halfSize);
            return result;
        }
        /// <summary>
        /// 奥林匹克滤波
        /// </summary>
        /// <param name="data"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static double[] OlympicFilter(double[] data, int size)
        {
            double[] result = new double[data.Length];
            int halfSize = size / 2;
            Array.Copy(data, 0, result, 0, halfSize);
            for (int i = halfSize; i < data.Length - halfSize; i++)
            {
                double[] window = new double[size];
                for (int j = -halfSize; j < halfSize; j++)
                {
                    window[j + halfSize] = data[i + j];
                }
                Array.Sort(window);
                double sum = 0;
                for (int j = 1; j < window.Length - 1; j++)
                {
                    sum += window[j];
                }
                int count = window.Length - 2;
                result[i] = sum / count;
            }
            Array.Copy(data, data.Length - halfSize, result, data.Length - halfSize, halfSize);
            return result;
        }
        /// <summary>
        /// 高保真中位数滤波
        /// </summary>
        /// <param name="data"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static double[] HighFidelityMedianFilter(double[] data, int size)
        {
            double[] result = new double[data.Length];
            int halfSize = size / 2;
            for (int i = halfSize; i < data.Length - halfSize; i++)
            {
                double[] window = new double[size];
                for (int j = -halfSize; j < halfSize; j++)
                {
                    window[j + halfSize] = data[i + j];
                }
                Array.Sort(window);
                double median = window[halfSize];
                double deviation = 0;
                for (int j = -halfSize; j < halfSize; j++)
                {
                    deviation += Math.Abs(window[j + halfSize] - median);
                }
                deviation /= size;
                double threshold = 3.4826 * deviation;
                int count = 0;
                double sum = 0;
                for (int j = -halfSize; j < halfSize; j++)
                {
                    if (Math.Abs(window[j + halfSize] - median) <= threshold)
                    {
                        sum += window[j + halfSize];
                        count++;
                    }
                }
                result[i] = sum / count;
            }
            Array.Copy(data, 0, result, 0, halfSize);
            Array.Copy(data, data.Length - halfSize, result, data.Length - halfSize, halfSize);
            return result;
        }
    }
}
