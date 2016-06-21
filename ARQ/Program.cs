using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
namespace ARQ
{
    class Program
    {
        static int sf, sn, rn, sw, dataLength;
        static ConcurrentQueue<int> stor = new ConcurrentQueue<int>();
        static ConcurrentQueue<int> rtos = new ConcurrentQueue<int>();
        static string MutexName = "fpcsong";
        static Mutex mutex = new Mutex(false, MutexName);
        static Random rand = new Random();
        static void Main(string[] args)
        {
            Console.SetWindowSize(160, 50);
            sf = sn = rn = 0;
            dataLength = 16;
            Console.WriteLine("Please input the window size of protocol and then maxmize the window to see the result:");
            while ((false == int.TryParse(Console.ReadLine(), out sw) || (sw > 15 || sw < 1))) { };
            new Thread(() => send()).Start();
            new Thread(() => recive()).Start();
            new Thread(() => recACK()).Start();
        }
        static void draw(int sf, int sn, int rn, string para)
        {
            mutex.WaitOne();
            if (sf == 16)
            {
                for (int i = 0; i < 16; i++) sended(i);
                Console.Write("{0,25}      ", para);
                for (int i = 0; i < 16; i++) sended(i);
                Console.WriteLine();
                mutex.ReleaseMutex();
                return;
            }
            for (int i = 0; i < sf; i++) sended(i);
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.Write("[ ");
            for (int i = sf; i < sn; i++) waitForResponse(i);
            for (int i = sn; i < Math.Min(sf + sw, dataLength); i++) dataInWindow(i);
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.Write(" ]");
            for (int i = sf + sw; i < dataLength; i++) unsend(i);
            Console.ResetColor();
            Console.Write("{0,25}      ", para);
            for (int i = 0; i < rn; i++) sended(i);
            if (rn < 16) waitForResponse(rn);
            for (int i = rn + 1; i < dataLength; i++) unsend(i);
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();
            mutex.ReleaseMutex();
        }
        static void unsend(int i)
        {
            Console.BackgroundColor = ConsoleColor.DarkGreen;
            Console.Write(i.ToString() + ' ');
        }
        static void sended(int i)
        {
            Console.BackgroundColor = ConsoleColor.Black;
            Console.Write(i.ToString() + ' ');
        }
        static void waitForResponse(int i)
        {
            Console.BackgroundColor = ConsoleColor.DarkRed;
            Console.Write(i.ToString() + ' ');
        }
        static void dataInWindow(int i)
        {
            Console.BackgroundColor = ConsoleColor.Green;
            Console.Write(i.ToString() + ' ');
        }
        /// <summary>
        /// send msg through stor 
        /// send 110 to instand missed msg
        /// </summary>
        static void send()
        {
            while (true)
            {
                while (sn - sf >= sw) { };
                stor.Enqueue(sn);
                Interlocked.Increment(ref sn);
                draw(sf, sn, rn, "Send msg " + (sn - 1).ToString());
                if (sn == 16)
                {
                    draw(sf, sn, rn, "Send completed");
                    //break;
                }
            }
        }
        /// <summary>
        /// 没能体现出来 小于sf的timeout是无效的这一点 使用队列做信道默认保证了有序性= =
        /// </summary>
        static void recACK()
        {
            while (true)
            {
                while (rtos.IsEmpty) { };
                int ack = 0;
                while (false == rtos.TryDequeue(out ack)) { };
                if (ack == 110)
                {
                    Interlocked.Add(ref sn, sf - sn);
                    draw(sf, sn, rn, "ack" + (sf + 1).ToString() + " missed,timeout");
                }
                else
                {
                    if (ack <= sn && ack > sf) sf = ack;
                    if (ack == 16)
                    {
                        sf = 16;
                        draw(sf, sn, rn, "All acks received");
                        ///怎么关闭整个程序呢
                        return;
                    }
                    draw(sf, sn, rn, "Receive ack " + ack.ToString());
                }
            }
        }
        /// <summary>
        /// receive mag and send ack 
        /// send 110 to instand missed ack
        /// </summary>
        static void recive()
        {
            while (true)
            {
                while (stor.IsEmpty) { };
                int msg = 0;
                while (false == stor.TryDequeue(out msg)) { };
                if (msg == 110) continue;
                if (msg == rn)
                {
                    Interlocked.Increment(ref rn);
                    if (rand.Next(0, 10) > 2) rtos.Enqueue(rn);
                    else rtos.Enqueue(110);
                    if (rn == 16)
                    {
                        draw(sf, sn, rn, "Receive completed");
                    }
                    else
                    {
                        draw(sf, sn, rn, "Receive msg " + msg.ToString());
                    }
                }
                else
                {
                    if (rand.Next(0, 10) > 2) rtos.Enqueue(rn);
                    else rtos.Enqueue(110);
                }
            }
        }
    }
}
