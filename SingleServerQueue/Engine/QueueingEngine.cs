namespace SingleServerQueue.Engine;

public enum DistributionType { Exponential, Uniform, Normal, Gamma, Deterministic }

// ── Random variate generators ──────────────────────────────────────────────────
internal sealed class Rng
{
    private readonly Random _r;
    public Rng(int seed = 42) => _r = new Random(seed);

    double Exp(double rate) => -Math.Log(Math.Max(_r.NextDouble(), 1e-15)) / rate;

    double Normal(double mean, double std)
    {
        double u1 = Math.Max(_r.NextDouble(), 1e-15);
        double z  = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * _r.NextDouble());
        return Math.Max(1e-9, mean + std * z);
    }

    double Gamma(double shape, double scale)
    {
        if (shape < 1.0) shape += 1.0;
        double d = shape - 1.0 / 3.0;
        double c = 1.0 / Math.Sqrt(9.0 * d);
        for (int i = 0; i < 1000; i++)
        {
            double x, v;
            do { x = Normal(0, 1); v = 1.0 + c * x; } while (v <= 0);
            v = v * v * v;
            double u = _r.NextDouble();
            if (u < 1.0 - 0.0331 * x * x * x * x)                      return d * v * scale;
            if (Math.Log(u) < 0.5 * x * x + d * (1 - v + Math.Log(v))) return d * v * scale;
        }
        return d * scale;
    }

    public double Sample(DistributionType dist, double mean, double variance) =>
        dist switch
        {
            DistributionType.Exponential   => Exp(1.0 / mean),
            DistributionType.Uniform       => mean - Math.Sqrt(3*variance) +
                                             2 * Math.Sqrt(3*variance) * _r.NextDouble(),
            DistributionType.Normal        => Normal(mean, Math.Sqrt(variance)),
            DistributionType.Gamma         => Gamma(mean*mean/variance, variance/mean),
            DistributionType.Deterministic => mean,
            _                              => Exp(1.0 / mean)
        };
}

// ── Simulation result ──────────────────────────────────────────────────────────
internal record SimOut(double Utilization, double AvgWait, int MaxQ, int Served, int Arrived);

// ── Discrete-event single-server FIFO simulation ──────────────────────────────
internal static class Sim
{
    public static SimOut Run(
        double meanIA, double varIA, DistributionType distIA,
        double meanS,  double varS,  DistributionType distS,
        int    T)
    {
        var rng = new Rng(42);
        double clock = 0, nextArr = rng.Sample(distIA, meanIA, varIA), nextDep = double.MaxValue;
        int inSys = 0, arrived = 0, served = 0, maxQ = 0;
        double busy = 0, busyStart = 0;
        var q  = new Queue<double>();
        var wt = new List<double>(8192);

        while (clock < T)
        {
            if (nextArr < nextDep && nextArr < T)
            {
                clock = nextArr; inSys++; arrived++;
                q.Enqueue(clock);
                if (inSys > maxQ) maxQ = inSys;
                if (inSys == 1) { busyStart = clock; nextDep = clock + rng.Sample(distS, meanS, varS); }
                nextArr = clock + rng.Sample(distIA, meanIA, varIA);
            }
            else if (nextDep <= T)
            {
                clock = nextDep;
                wt.Add(clock - q.Dequeue());
                served++; inSys--;
                if (inSys == 0) { busy += clock - busyStart; nextDep = double.MaxValue; }
                else nextDep = clock + rng.Sample(distS, meanS, varS);
            }
            else break;
        }

        return new SimOut(busy / T,
            wt.Count > 0 ? wt.Average() : 0.0,
            maxQ, served, arrived);
    }
}

// ── Public engine ──────────────────────────────────────────────────────────────
public static class QueueingEngine
{
    public static SimResponse Compute(SimRequest req) =>
        req.Model.ToLowerInvariant() switch
        {
            "mm1" => MM1(req),
            "mg1" => MG1(req),
            "gg1" => GG1(req),
            _     => throw new ArgumentException($"Unknown model: {req.Model}")
        };

    // ══════════════════════════════════════════════════════════════════════════
    // M/M/1  —  Example 1 from notes
    //
    // Given:  E[A] = 10 min  →  λ = 1/10 = 0.1
    //         E[S] = 8 min   →  μ = 1/8  = 0.125
    //
    // ρ  = λ/μ = 0.8
    // Lq = ρ² / (1-ρ)         = 0.64/0.2        = 3.2
    // Wq = Lq / λ             = 3.2/0.1          = 32 min
    // W  = Wq + 1/μ = Wq+E[S] = 32+8             = 40 min
    // L  = λ·W                = 0.1×40            = 4
    // P0 = 1-ρ                                   = 0.2
    // ══════════════════════════════════════════════════════════════════════════
    private static SimResponse MM1(SimRequest req)
    {
        double ea  = req.MeanArrival;   // E[A] = mean inter-arrival time
        double es  = req.MeanService;   // E[S] = mean service time
        double lam = 1.0 / ea;          // λ = arrival rate
        double mu  = 1.0 / es;          // μ = service rate
        double rho = lam / mu;          // ρ = λ/μ  =  E[S]/E[A]
        bool   ok  = rho < 1.0;

        double lq = ok ? rho * rho / (1 - rho) : 9999;
        double wq = ok ? lq / lam               : 9999;  // Wq = Lq/λ
        double w  = ok ? wq + es                : 9999;  // W  = Wq + E[S]
        double l  = ok ? lam * w                : 9999;  // L  = λW
        double p0 = 1 - rho;

        var sim = Sim.Run(ea, ea*ea, DistributionType.Exponential,
                          es, es*es, DistributionType.Exponential, req.SimTime);

        return new SimResponse("M/M/1",
            R(lam), R(mu), R(rho), ok,
            R(lq), R(l), R(wq), R(w), R(p0),
            R(sim.Utilization), R(sim.AvgWait),
            sim.MaxQ, sim.Served, sim.Arrived,
            1.0, null,
            $"Lq = ρ²/(1-ρ) = {rho:F4}²/{1-rho:F4} = {R(lq):F4}");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // M/G/1  —  Example 2 from notes
    //
    // Given:  E[A]=10, E[S]=8, service = Uniform(7,9)
    //         σs² = (9-7)²/12 = 4/12 = 1/3
    //
    // λ=0.1, μ=0.125, ρ=0.8
    //
    // P-K formula:
    //   Lq = (λ²·σs² + ρ²) / (2(1-ρ))
    //      = (0.01·(1/3) + 0.64) / (2·0.2)
    //      = (0.003333 + 0.64) / 0.4
    //      = 1.608
    //   Wq = Lq/λ = 16.08 min
    //   W  = Wq + E[S] = 24.08 min
    //   L  = λW = 2.408
    //   P0 = 1-ρ = 0.2
    // ══════════════════════════════════════════════════════════════════════════
    private static SimResponse MG1(SimRequest req)
    {
        double ea   = req.MeanArrival;
        double es   = req.MeanService;
        double vs   = req.ServiceVariance;   // σs²
        double lam  = 1.0 / ea;
        double mu   = 1.0 / es;
        double rho  = lam / mu;
        bool   ok   = rho < 1.0;
        double cs2  = vs / (es * es);        // Cs² = σs²/E[S]²

        // P-K:  Lq = (λ²σs² + ρ²) / (2(1-ρ))
        double lq = ok ? (lam*lam*vs + rho*rho) / (2*(1-rho)) : 9999;
        double wq = ok ? lq / lam  : 9999;
        double w  = ok ? wq + es   : 9999;
        double l  = ok ? lam * w   : 9999;
        double p0 = 1 - rho;

        DistributionType distS = Parse(req.ServiceDist);
        var sim = Sim.Run(ea, ea*ea, DistributionType.Exponential,
                          es, vs,    distS, req.SimTime);

        return new SimResponse($"M/G/1 ({req.ServiceDist})",
            R(lam), R(mu), R(rho), ok,
            R(lq), R(l), R(wq), R(w), R(p0),
            R(sim.Utilization), R(sim.AvgWait),
            sim.MaxQ, sim.Served, sim.Arrived,
            R(cs2), null,
            $"Lq = (λ²σs²+ρ²)/(2(1-ρ)) = ({lam*lam:F5}×{vs:F4}+{rho*rho:F4})/{2*(1-rho):F4} = {R(lq):F4}");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // G/G/1  —  Example 3 from notes
    //
    // Given:  E[A]=10, σa²=20 (Gamma),  E[S]=8, σs²=25 (Normal)
    //
    // λ=0.1, μ=0.125, ρ=0.8
    // Ca² = σa²/E[A]² = 20/100  = 0.2
    // Cs² = σs²/E[S]² = 25/64   = 0.390625
    //
    // Marchal's approximation (Eq.2 from notes):
    //   Lq = ρ²(1+Cs²)(Ca²+ρ²Cs²) / (2(1-ρ)(1+ρ²Cs²))
    //
    // Verify:
    //   ρ²=0.64,  ρ²Cs²=0.64×0.390625=0.25
    //   num = 0.64 × 1.390625 × (0.2+0.25) = 0.64×1.390625×0.45 = 0.40050
    //   den = 2×0.2×(1+0.25) = 0.5
    //   Lq  = 0.40050/0.5 = 0.8010  ✓
    //   Wq  = 0.8010/0.1  = 8.01 min  (simulation says ≈8.1)  ✓
    // ══════════════════════════════════════════════════════════════════════════
    private static SimResponse GG1(SimRequest req)
    {
        double ea  = req.MeanArrival;
        double va  = req.ArrivalVariance;    // σa²
        double es  = req.MeanService;
        double vs  = req.ServiceVariance;    // σs²
        double lam = 1.0 / ea;
        double mu  = 1.0 / es;
        double rho = lam / mu;
        bool   ok  = rho < 1.0;

        double ca2 = va / (ea * ea);         // Ca² = σa²/E[A]²
        double cs2 = vs / (es * es);         // Cs² = σs²/E[S]²

        double lq, wq, w, l;
        if (ok)
        {
            double r2  = rho * rho;
            // Marchal Eq.2:  Lq = ρ²(1+Cs²)(Ca²+ρ²Cs²) / (2(1-ρ)(1+ρ²Cs²))
            double num = r2 * (1 + cs2) * (ca2 + r2 * cs2);
            double den = 2.0 * (1 - rho) * (1 + r2 * cs2);
            lq = num / den;
            wq = lq / lam;
            w  = wq + es;
            l  = lam * w;
        }
        else { lq = wq = w = l = 9999; }

        double p0 = 1 - rho;

        DistributionType distA = Parse(req.ArrivalDist);
        DistributionType distS = Parse(req.ServiceDist);
        var sim = Sim.Run(ea, va, distA, es, vs, distS, req.SimTime);

        return new SimResponse($"G/G/1 (A:{req.ArrivalDist}, S:{req.ServiceDist})",
            R(lam), R(mu), R(rho), ok,
            R(lq), R(l), R(wq), R(w), R(p0),
            R(sim.Utilization), R(sim.AvgWait),
            sim.MaxQ, sim.Served, sim.Arrived,
            R(cs2), R(ca2),
            $"Marchal: Ca²={ca2:F4}, Cs²={cs2:F4}, Lq={R(lq):F4}");
    }

    static double R(double v) =>
        double.IsInfinity(v) || double.IsNaN(v) ? 9999 : Math.Round(v, 6);

    static DistributionType Parse(string s) =>
        s.ToLowerInvariant() switch
        {
            "uniform"       => DistributionType.Uniform,
            "normal"        => DistributionType.Normal,
            "gamma"         => DistributionType.Gamma,
            "deterministic" => DistributionType.Deterministic,
            _               => DistributionType.Exponential
        };
}
