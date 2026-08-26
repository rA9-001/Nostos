using System.Security.Cryptography;
using System.Text;

// Maintainer tooling for cutting a release: generate the signing key, and sign a checksum file
// with it. Nothing here ships to anybody; the product never references this project.
//
// This started life as two PowerShell scripts and stopped being PowerShell for a concrete
// reason. Windows PowerShell 5.1 -- still the default `powershell.exe` on Windows 11 -- runs on
// .NET Framework 4.8, which has no ExportSubjectPublicKeyInfo, no ImportFromPem, and no
// DSASignatureFormat. The scripts therefore worked in CI, where the shell is PowerShell 7, and
// failed on the maintainer's own machine, which is the worst possible split for a tool whose
// entire job is to be run by hand once and then trusted.
//
// A console project has none of that: the repo already requires the .NET 10 SDK, so this runs
// identically on a laptop and on a runner.

return args.FirstOrDefault()?.ToLowerInvariant() switch
{
    "keygen" => KeyGen(args.Skip(1).ToArray()),
    "sign" => Sign(args.Skip(1).ToArray()),
    "verify" => Verify(args.Skip(1).ToArray()),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("""
    Release tooling. Run from the repository root with `dotnet run --project tools/Nostos.ReleaseTool -- <verb>`.

      keygen [--out <file>]
          Generate the ECDSA P-256 release signing key. Prints the PUBLIC half to paste into
          src/Nostos.Core/Updates/ReleaseIntegrity.cs, and the PRIVATE half to store as the
          repository secret NOSTOS_SIGNING_KEY. Run this once, ever.

      sign --checksums <file> (--key-file <file> | --key-env <VAR>)
          Write <file>.sig: a base64 ECDSA signature over the checksum file's exact bytes.

      verify --checksums <file> --signature <file> --public-key <base64>
          Check a signature the way the updater will. Use it before publishing.
    """);
    return 2;
}

static string? Arg(string[] args, string name)
{
    var index = Array.FindIndex(args, a => a.Equals("--" + name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static int KeyGen(string[] args)
{
    using var key = ECDsa.Create(ECCurve.CreateFromFriendlyName("nistP256"));

    var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    var privateKey = key.ExportPkcs8PrivateKeyPem();

    Console.WriteLine();
    Console.WriteLine("PUBLIC KEY - paste into src/Nostos.Core/Updates/ReleaseIntegrity.cs and commit:");
    Console.WriteLine();
    Console.WriteLine($"    public const string SigningPublicKeyBase64 = \"{publicKey}\";");
    Console.WriteLine();

    if (Arg(args, "out") is { } path)
    {
        // No trailing newline: the file is read back verbatim and re-parsed, and a stray byte
        // is the sort of thing that works on one machine and not another.
        File.WriteAllText(path, privateKey, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"PRIVATE KEY - written to {path}");
        Console.WriteLine();
        Console.WriteLine("  Store it as the repository secret, then delete the file:");
        Console.WriteLine($"      gh secret set NOSTOS_SIGNING_KEY < \"{path}\"");
        Console.WriteLine($"      del \"{path}\"");
    }
    else
    {
        Console.WriteLine("PRIVATE KEY - store as the repository secret NOSTOS_SIGNING_KEY. Never commit it.");
        Console.WriteLine();
        Console.WriteLine(privateKey);
    }

    Console.WriteLine();
    Console.WriteLine("Changing this key later orphans everyone on an older build: their copy will refuse");
    Console.WriteLine("releases signed by the new key. Back the private half up somewhere durable.");
    Console.WriteLine();
    return 0;
}

static int Sign(string[] args)
{
    if (Arg(args, "checksums") is not { } checksums || !File.Exists(checksums))
    {
        Console.Error.WriteLine("sign: --checksums <file> is required and must exist.");
        return 2;
    }

    var pem = Arg(args, "key-file") is { } keyFile
        ? File.ReadAllText(keyFile)
        : Arg(args, "key-env") is { } variable
            ? Environment.GetEnvironmentVariable(variable)
            : null;

    if (string.IsNullOrWhiteSpace(pem))
    {
        Console.Error.WriteLine("sign: give --key-file <file> or --key-env <VAR>.");
        return 2;
    }

    // The signature covers the file's raw bytes. Reading text and re-encoding it would make the
    // signature depend on this machine's line endings, and it would verify here and nowhere else.
    var content = File.ReadAllBytes(checksums);

    using var key = ECDsa.Create();
    key.ImportFromPem(pem);

    // Rfc3279DerSequence, named explicitly: the default is the fixed-size IEEE P1363 encoding,
    // the two are not interchangeable, and the updater verifies with this one.
    var signature = key.SignData(content, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

    var output = checksums + ".sig";
    File.WriteAllText(output, Convert.ToBase64String(signature), new UTF8Encoding(false));

    Console.WriteLine($"signed  {output}");
    return 0;
}

static int Verify(string[] args)
{
    if (Arg(args, "checksums") is not { } checksums
        || Arg(args, "signature") is not { } signature
        || Arg(args, "public-key") is not { } publicKey)
    {
        Console.Error.WriteLine("verify: --checksums, --signature and --public-key are all required.");
        return 2;
    }

    var content = File.ReadAllBytes(checksums);
    var raw = Convert.FromBase64String(File.ReadAllText(signature).Trim());

    using var key = ECDsa.Create();
    key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);

    var ok = key.VerifyData(content, raw, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

    Console.WriteLine(ok ? "signature verifies" : "SIGNATURE DOES NOT VERIFY");
    return ok ? 0 : 1;
}
