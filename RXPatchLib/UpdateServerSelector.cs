﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace RXPatchLib
{
    public class UpdateServerSelector
    {
        CancellationTokenSource CancellationTokenSource = new CancellationTokenSource();

        int? bestHostIndex = null;
        long bestHostRtt = 0;
        object bestHostLock = new object();

        async Task PingHost(string host, int index)
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync(host, 5000, CancellationTokenSource.Token);


                    System.Net.HttpWebRequest request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(new Uri("http://" + host));
                    request.Method = "GET";
                    System.Net.HttpStatusCode response = System.Net.HttpStatusCode.NotFound;
                    try
                    {
                        response = ((System.Net.HttpWebResponse)request.GetResponse()).StatusCode;
                    }
                    catch
                    {
                        Trace.WriteLine(string.Format("<!><!><!>The host: {0} is down.", host));
                    }

                    if (response == System.Net.HttpStatusCode.OK)
                    {
                        Trace.WriteLine(string.Format("Ping to {0}: {1}.", host, reply.RoundtripTime));
                        lock (bestHostLock)
                        {
                            if (!bestHostIndex.HasValue || reply.RoundtripTime < bestHostRtt)
                            {
                                bestHostRtt = reply.RoundtripTime;
                                bestHostIndex = index;
                            }
                        }
                    }       
                }
            }
            catch (PingException)
            {
                Trace.WriteLine(string.Format("Ping to {0} failed.", host));
            }
            catch (OperationCanceledException)
            {
                Trace.WriteLine(string.Format("Ping to {0} canceled.", host));
            }
        }

        /// <summary>
        /// Select the host with the lowest ping.
        /// 
        /// Allows all hosts at least 500 ms to reply. After 500 ms (earlier if possible), the best server is selected.
        /// Allows all hosts to reply within 100 ms after the best host replied, to avoid incorrect results due to scheduling. (Somewhat pedantic.)
        /// If no pings were received within the default system timeout, a random server is selected.
        /// </summary>
        /// <param name="hosts"></param>
        /// <returns></returns>
        public async Task<int> SelectHostIndex(ICollection<string> hosts)
        {
            Contract.Assume(hosts.Count > 0);

            if (hosts.Count == 1)
            {
                return 0;
            }

            Task[] pingTasks = hosts.Select((host, index) => PingHost(host, index)).ToArray();
            await Task.WhenAll(pingTasks).ProceedAfter(500);
            foreach (var task in pingTasks)
            {
                task
                    .ContinueWith((result) =>
                    {
                        CancellationTokenSource.CancelAfter(100);
                    }, TaskContinuationOptions.OnlyOnRanToCompletion)
                    .Forget();
            }

            await Task.WhenAll(pingTasks).ProceedIfCanceled().CancelAfter(5000);
            if (bestHostIndex == null)
            {      
                throw new Exception("Could not select a reliable downloadserver. Please try again later...");
            }

            return (int)bestHostIndex;// ?? new Random().Next(hosts.Count); //This random needs to change. It can select a dead server!
        }
    }
}
