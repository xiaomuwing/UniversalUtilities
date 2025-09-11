using System;

namespace UniversalUtilities
{
    /// <summary>
    /// 获取非托管内存中数组的统计值
    /// </summary>
    public class ArrayManipulator
    {
        public static unsafe double[] CutArray(double* inputArray, int startIndex, int endIndex)
        {

            int length = endIndex - startIndex + 1;
            double[] outputArray = new double[length];
            double* pSrc = inputArray + startIndex;
            fixed (double* pDst = &outputArray[0])
            {
                for (int i = 0; i < length; i++)
                {
                    pDst[i] = pSrc[i];
                }
            }
            return outputArray;
        }
        /// <summary>
        /// 获取非托管内存中数组的统计值(最大值，最小值，合计，平均值，绝对平均值，峰峰值，半峰值，标准差，均方根)
        /// </summary>
        /// <param name="inputArray"></param>
        /// <param name="begin"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        public static unsafe (double, double, double, double, double, double, double, double, double) GetStatistic(double* inputArray, int begin, int end)
        {
            double maxValue = double.MinValue;
            double minValue = double.MaxValue;
            double sum = 0;
            double avg = 0;
            double absAvg = 0;
            double ppValue = 0;
            double halfpp = 0;
            double variance = 0;
            double std = 0;
            double rms = 0;




            double* pSrc = inputArray + begin;
            double num = 0.0;
            double num2 = 0.0;
            double num4 = 0;
            ulong num5 = 0uL;

            for (int i = begin; i < end; i++)
            {
                if (*pSrc > maxValue)
                {
                    maxValue = *pSrc;
                }
                if (*pSrc < minValue)
                {
                    minValue = *pSrc;
                }
                sum += *pSrc;
                absAvg += Math.Abs(*pSrc);

                int j = i - begin + 1;
                num2 += *pSrc;
                double num3 = (j + 1.0) * *pSrc - num2;
                num += num3 * num3 / ((j + 1.0) * j);

                num4 += (*pSrc * *pSrc - num4) / (double)(++num5);

                pSrc++;
            }
            avg = sum / (end - begin + 1);
            absAvg = absAvg / (end - begin + 1);
            ppValue = maxValue - minValue;
            halfpp = ppValue / 2;
            variance = num / (end - begin + 1);
            std = Math.Sqrt(variance);
            rms = Math.Sqrt(num4);
            return (maxValue, minValue, sum, avg, absAvg, ppValue, halfpp, std, rms);
        }
        public static unsafe double GetMaxValue(double* inputArray, int begin, int end)
        {
            double maxValue = double.MinValue;
            double minValue = double.MaxValue;
            double sum = 0;
            double avg = 0;
            double absSum = 0;
            double ppValue = 0;
            double halfpp = 0;
            double variance = 0;
            double std = 0;
            double rms = 0;




            double* pSrc = inputArray + begin;
            double num = 0.0;
            double num2 = *pSrc;
            double num4 = 0;
            ulong num5 = 0uL;

            for (int i = begin; i < end; i++)
            {
                if (*pSrc > maxValue)
                {
                    maxValue = *pSrc;
                }
                if (*pSrc < minValue)
                {
                    minValue = *pSrc;
                }
                sum += *pSrc;
                absSum += Math.Abs(*pSrc);

                num2 += *pSrc;
                double num3 = (i + 1) * *pSrc - num2;
                num += num3 * num3 / ((i + 1.0) * i);
                num4 += (*pSrc * *pSrc - num4) / (double)(++num5);
                pSrc++;
            }
            avg = sum / (end - begin + 1);
            ppValue = maxValue - minValue;
            halfpp = ppValue / 2;
            variance = num / (end - begin);
            std = Math.Sqrt(variance);
            rms = Math.Sqrt(num4);
            return maxValue;
        }
        public static unsafe double GetMinValue(double* inputArray, int length)
        {
            double minValue = double.MaxValue;
            double* pSrc = inputArray;
            for (int i = 0; i < length; i++)
            {
                if (*pSrc < minValue)
                {
                    minValue = *pSrc;
                }
                pSrc++;
            }
            return minValue;
        }
        public static unsafe double GetSumValue(double* inputArray, int length)
        {
            double sum = 0;
            double* pSrc = inputArray;
            for (int i = 0; i < length; i++)
            {
                sum += *pSrc++;
            }
            return sum;
        }
        public static unsafe double GetAverageValue(double* inputArray, int length)
        {
            double sum = 0;
            double* pSrc = inputArray;
            for (int i = 0; i < length; i++)
            {
                sum += *pSrc++;
            }
            return sum / length;
        }
        public static unsafe double GetAbsoluteAverageValue(double* inputArray, int length)
        {
            double sum = 0;
            double* pSrc = inputArray;
            for (int i = 0; i < length; i++)
            {
                sum += Math.Abs(*pSrc++);
            }
            return sum / length;
        }
        public static unsafe double GetPeakToPeakValue(double* inputArray, int length)
        {
            double maxValue = double.MinValue;
            double minValue = double.MaxValue;
            double* pSrc = inputArray;
            for (int i = 0; i < length; i++)
            {
                if (*pSrc > maxValue)
                {
                    maxValue = *pSrc;
                }
                if (*pSrc < minValue)
                {
                    minValue = *pSrc;
                }
                pSrc++;
            }
            return maxValue - minValue;
        }
        public static unsafe double GetHalfPeakValue(double* inputArray, int length)
        {
            double peakValue = GetPeakToPeakValue(inputArray, length);
            return peakValue / 2;
        }
    }
}
