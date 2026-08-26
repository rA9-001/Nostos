# Measuring

`nos bench` measures network latency and tells you whether two measurements actually differ.

It exists for one reason, and it is not the obvious one. It is not here to prove that a tweak
worked. It is here to catch the two outcomes no tweaking tool ever reports: **the change that did
nothing**, and **the change that made things worse**.

```
nos bench --label "before"
nos apply network.interrupt-moderation-off
# reboot
nos bench --label "after"
nos bench compare
```

## What it reports, and why not the average

```
Round trip
  median       12.49 ms      the typical case
  p95          12.96 ms      1 packet in 20 was at least this late
  p99          13.15 ms      1 in 100 - this is what you feel
  min / max    11.98 / 17.27 ms

Steadiness
  jitter       0.37 ms       mean change between consecutive packets
  loss         0.0%          100 of 100 answered
```

The mean is not shown anywhere. Nobody notices their average round trip; they notice the packets
that arrived late, because those are the ones that become a rubber-band or a shot that did not
register. That is the tail, so the tail is what gets printed.

**Jitter is the number to watch.** It is the mean absolute difference between consecutive
packets, as RFC 3550 defines it — not "max minus min". Consider two connections:

| | median | min | max | jitter |
| --- | --- | --- | --- | --- |
| Steady | 20 ms | 20 | 60 | 6.7 ms |
| Alternating | 20 ms | 20 | 60 | 40 ms |

Identical median, identical min, identical max. The first is fine and the second is unplayable,
and jitter is the only column that says so. A game's interpolation buffer has to absorb the
change from one packet to the next; a steady 60 ms is far easier to play on than something that
alternates between 20 and 60.

## The verdict

```
Verdict  no measurable difference (95% CI -0.11 to 0.12 ms spans zero)
```

This is the part that matters, and it is the part every other tool gets wrong. Ten samples
before, ten after, the median moves 3%, and the tool announces an improvement. Latency varies by
more than that between two runs where *nothing changed at all* — so a tool that does not test for
it will report a win for any change, including a change that did nothing, including a change that
made things worse.

`compare` resamples both runs two thousand times (a bootstrap) and reports a 95% confidence
interval for the change in median. **If that interval contains zero, the answer is "cannot
tell".** Bootstrapping rather than a t-test because latency is not normally distributed: it has a
hard floor at the speed of light and a long right tail, which is the shape a t-test handles
worst. Resampling assumes nothing about the shape.

The seed is fixed, so the same two runs always produce the same verdict. A number you might have
to justify to somebody should not change when you look at it again.

### Two runs with nothing changed is a useful test

Run `nos bench` twice in a row and compare them. Whatever interval comes back is your
connection's noise floor — the smallest difference this measurement could ever detect on your
line. On the connection the example above came from it was about ±0.12 ms. Anything smaller than
your noise floor is not measurable here, no matter how many times you re-run it.

## What it cannot tell you

**Most of the Ping category will come back "no measurable difference", and that is correct.**

The adapter tweaks — interrupt moderation, receive coalescing, energy-efficient ethernet — remove
delays measured in *microseconds*, on the local machine. The internet leg of a real connection is
*milliseconds*, and it varies by more than the entire effect between one minute and the next.
Looking for a 50 µs change through 12 ms of internet is looking for a coin on the seabed.

That is not a reason to skip measuring. It is a reason to know what the measurement is for:

- It will catch a change that costs you 5 ms. Several can.
- It will catch a background download, a Wi-Fi link renegotiating, or Delivery Optimization
  saturating your upstream — all of which are worth far more than any tweak on the list.
- It gives you a baseline, so a regression six months from now has something to be compared
  against.

If you want to see the microsecond effects, you need to measure on the same LAN as the target, so
that the local machine is most of the round trip. `nos bench --host 192.168.1.1` against your own
router is the cheapest version of that.

## The two probe kinds

| | What it measures |
| --- | --- |
| `--icmp` | ICMP echo. What `ping` does. |
| default | Time to complete a TCP handshake to a port and close it. |

TCP is the default because ICMP is misleading on its own: routers routinely handle it on a slow
path, rate-limit it, or answer on the destination's behalf. A handshake travels the path that
game traffic travels.

Disagreement between the two is informative rather than a fault — if ICMP is much worse than TCP,
something between you and the target is deprioritising it, and your real ping is the better
number.

The default target is `1.1.1.1:853`. It is anycast so it is near almost everybody, it is not a
CDN edge that might be sitting inside your ISP, and something really is listening on 853, so the
handshake completes rather than being refused. **A server you actually play on is a better
target** — `--host` and `--port` take one.

The first three samples of every run are taken and thrown away. The first packet to a host pays
for ARP or neighbour discovery, a DNS lookup, and on Wi-Fi possibly a power-save wake-up;
including it would make every run's maximum a measurement of cold start.

Samples are 50 ms apart. Back-to-back probes measure how fast the local stack can loop, and they
invite every router in the path to start rate-limiting them, which then reads as packet loss that
is not there.

## Where it is kept

`%ProgramData%\Nostos\benchmarks.jsonl` — append-only, one JSON object per line, the same format
and the same reasoning as the change journal beside it.

Each run stores **the tweak ids that were applied when it was taken**, read from the journal. That
is what makes the history worth keeping: a latency number on its own is a number, and a latency
number next to the machine state that produced it is evidence. `compare` prints what changed
between two runs, so you are never relying on your memory of what you had done in between.

Every successful sample is kept, not just the summary — a comparison has to resample the
originals, and percentiles cannot be resampled. Two hundred doubles is 1.6 KB.

Nothing prunes this file. A baseline from six months ago is the most valuable row in it.

## FPS

Not implemented. See the note at the end of [architecture.md](architecture.md) for why it is
harder than it looks and what it would take — the short version is that measuring another
process's framerate without injecting into it means consuming ETW events, which is a real
subsystem rather than an afternoon.
