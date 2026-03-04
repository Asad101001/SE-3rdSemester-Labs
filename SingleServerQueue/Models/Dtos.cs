namespace SingleServerQueue;

// ── What the frontend sends ────────────────────────────────────────────────────
// MeanArrival = E[A] = mean inter-arrival time in minutes  (e.g. 10)
// MeanService = E[S] = mean service time in minutes        (e.g. 8)
// ServiceVariance = sigma_s^2  (e.g. 1/3 for Uniform(7,9))
// ArrivalVariance = sigma_a^2  (e.g. 20 for G/G/1 Example 3)
public record SimRequest(
    string  Model,            // "mm1" | "mg1" | "gg1"
    double  MeanArrival,      // E[A] — mean inter-arrival time (minutes)
    double  MeanService,      // E[S] — mean service time (minutes)
    int     SimTime,          // simulation duration (minutes)
    double  ServiceVariance,  // sigma_s^2
    double  ArrivalVariance,  // sigma_a^2  (G/G/1 only)
    string  ServiceDist,      // "exponential"|"uniform"|"normal"|"gamma"
    string  ArrivalDist       // "exponential"|"gamma"|"normal"|"uniform"  (G/G/1 only)
);

// ── What the backend returns ───────────────────────────────────────────────────
public record SimResponse(
    string  ModelLabel,
    double  Lambda,           // arrival rate = 1/E[A]
    double  Mu,               // service rate = 1/E[S]
    double  Rho,              // traffic intensity
    bool    IsStable,
    double  Lq,               // mean number in queue
    double  L,                // mean number in system
    double  Wq,               // mean wait in queue (min)
    double  W,                // mean wait in system (min)
    double  P0,               // proportion server idle
    double  SimUtilization,
    double  SimAvgWait,
    int     SimMaxQueue,
    int     SimTotalServed,
    int     SimTotalArrivals,
    double? Cs2,
    double? Ca2,
    string  FormulaNote
);
