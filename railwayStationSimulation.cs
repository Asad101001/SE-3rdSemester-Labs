using System;

class MM1RailwaySimulation
{
    static Random rng = new Random();

    static double Exp(double rate)
    {
        return -Math.Log(1.0 - rng.NextDouble()) / rate;
    }

    static void Main()
    {
        double lambda = 5.0;   // arrivals per hour
        double mu = 8.0;       // services per hour
        double simEnd = 100.0; // hours

        double time = 0.0;
        double nextArrival = Exp(lambda);
        double nextDeparture = double.PositiveInfinity;

        int queue = 0;
        bool serverBusy = false;

        int served = 0;
        double busyTime = 0.0;
        double lastEventTime = 0.0;
        double areaQueue = 0.0;

        while (time < simEnd)
        {
            if (nextArrival < nextDeparture)
            {
                time = nextArrival;

                areaQueue += queue * (time - lastEventTime);
                if (serverBusy) busyTime += (time - lastEventTime);
                lastEventTime = time;

                nextArrival = time + Exp(lambda);

                if (!serverBusy)
                {
                    serverBusy = true;
                    nextDeparture = time + Exp(mu);
                }
                else
                {
                    queue++;
                }
            }
            else
            {
                time = nextDeparture;

                areaQueue += queue * (time - lastEventTime);
                busyTime += (time - lastEventTime);
                lastEventTime = time;

                served++;

                if (queue > 0)
                {
                    queue--;
                    nextDeparture = time + Exp(mu);
                }
                else
                {
                    serverBusy = false;
                    nextDeparture = double.PositiveInfinity;
                }
            }
        }

        double Lq = areaQueue / time;
        double utilization = busyTime / time;

        Console.WriteLine("Passengers served: " + served);
        Console.WriteLine("Average queue length Lq: " + Lq);
        Console.WriteLine("Server utilization: " + utilization);
    }
}

    