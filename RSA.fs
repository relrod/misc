﻿open System.Numerics

let bigint (x:int) = bigint(x) // Oddities  with F#

let KEYSIZE = 64
let HLEN = 32 // Length in octets of hash output, in this case SHA-256


let modexp a b n = //from Rosetta Code, calculates a^b (mod n)
    let mulMod x y n = snd (BigInteger.DivRem(x * y, n))
    let rec loop a b c =
        if b = 0I then c else
            let (bd, br) = BigInteger.DivRem(b, 2I)
            loop (mulMod a a n) bd (if br = 0I then c else (mulMod c a n))
    loop a b 1I


let modulo n m = // Because F#'s modulo doesn't handle negatives properly (-367 % 3120 = 2753, not -367!)
    let mod' = n % m
    if sign mod' >= 0 then mod'
    else abs m + mod'

let rec gcd (x: bigint) (y: bigint) = // Greatest common divisor, recursive Euclidean algorithm
    if y = 0I then x
    else gcd y (x % y)

let random n = // Generate a random number n bytes long
    let mutable bytes : byte array = Array.zeroCreate n
    (new System.Random()).NextBytes(bytes)
    abs (new BigInteger (bytes))

let rec ext_gcd (x:bigint) (y:bigint) = // Solve for the equation ax + by = gcd(a,b). When a nd b are coprime (i.e
                                        // gcd(a,b) = 0, x is the modular multiplicative inverse of a modulo b. 
                                        // This is crucial for calculating the private exponent. Implements the 
                                        // Extended Euclidean algorithm.
    if y = 0I then
        (1I,0I)
    else
        let q = x/y
        let r = x - y*q
        let (s,t) = ext_gcd y r
        (t, s-q*t)


let coprime n = // Find a coprime of n (gcd(n, x) = 1)
    let mutable test = random KEYSIZE
    while ((gcd n test) <> 1I) do 
        test <- random KEYSIZE
    test

let rec prime () = // Find a (probable) prime
    let x = random KEYSIZE
    let y = coprime x
    if modexp y (x-1I) x = 1I then x
    else prime ()


let modmultinv (e:bigint) (n:bigint) = // Find the modular multiplicative inverse of e mod n. That is, e * x = 1 (mod n)
    let (x,_) = ext_gcd e n
    modulo x n

let keys () = 
    let p = prime() // Pick an arbitrary large prime
    let mutable q = prime() // Pick another one
    while p = q do q <- prime() // Make sure they're different
    let n = p*q // This number is used as the modulus for the keys
    let phi = (p-1I)*(q-1I) // Euler's totient function. I am not very sure why this is used, but it is, so there
    let e = 65537I // 65537 is prime and therefore coprime to everything, and suitably large
    let d = modmultinv e phi
    ((n,e), (n,d))

let (pubkey, privkey) = keys()

let i2osp (x:bigint) len = // Convert a nonnegative integer to an octet string of a certain length (see RFC 3447)
    if x >= 256I**len then raise (System.ArgumentException("x must be < 256^len"))
    let mutable divrem = BigInteger.DivRem(x, 256I);
    let mutable octets : byte list = []
    while (fst divrem) <> 0I do
        octets <- List.append octets [(snd divrem) |> byte]
        divrem <- BigInteger.DivRem((fst divrem), 256I)

    octets <- List.append octets [(snd divrem) |> byte]
    divrem <- BigInteger.DivRem((fst divrem), 256I)
    while List.length octets < len do octets <- List.append octets [(byte 0)]
    List.rev octets


let rec os2ip (octets : byte list) (n: bigint) = // Convert an octet string to a nonnegative integer.
    match octets with
    | [] -> n
    | X::XS ->
    let acc = n + (X |> int |> bigint) * (256I ** (List.length XS))
    os2ip XS acc


let rsaep m (n, e) = // RSA Encryption Primitive    
    if m < 0I || m > (n-1I) then raise (System.ArgumentException("m out of range"))
    modexp m e n 
   
let rsadp c (n, d) = // RSA Decryption Primitive
    if  c < 0I || c > (n-1I) then raise (System.ArgumentException("c out of range"))
    modexp c d n

let hash (bytes: byte array) = (SHA256.Create()).ComputeHash(bytes);

let mgf seed len = //Mask Generation Function, see Section B.2.1 of RFC 3447
    if len > 2I**32 * (HLEN|>bigint) then raise (System.ArgumentException("mask too long"))
    let lenfloat = len |> float;
    let hlenfloat = HLEN |> float;
    let final = ceil((lenfloat/hlenfloat)-1.0) |> int
    let x = [for c in [0..final] ->  hash (Array.concat [|seed; List.toArray (i2osp (c|>bigint) 4)|])]
    let T = Array.concat x
    T.[0..((len-1I)|>int)]



let rsaes_oaep_encrypt ((n,e) as key) M L = // RSA OAEP Encryption. See Section 7.1 of RFC 3447. M and L should be provided as octet strings
    let k = List.length (i2osp n (KEYSIZE*2)) // Length of modulus in octets
    if (Array.length M) > (k - 2*HLEN - 2) then raise (System.ArgumentException("message too long"))
    printfn "%A" M
    let lHash = hash L
    let PS : byte array = Array.zeroCreate (k - (Array.length M) - 2 * HLEN - 2)
    let DB = Array.concat [| lHash; PS; [|0x01 |> byte|]; M|]
    printfn "%d" (Array.length DB)
    let seed = List.toArray (i2osp (random HLEN) HLEN)
    let dbMask = mgf seed ((k-HLEN-1) |> bigint)
    let maskedDB = Array.map2 (fun x y -> x ^^^ y) DB dbMask
    let seedMask = mgf maskedDB (HLEN|>bigint)
    let maskedSeed = Array.map2 (fun x y -> x ^^^ y) seed seedMask
    let EM = Array.concat [| [| (byte) 0 |]; maskedSeed; maskedDB |]
    let m = os2ip (Array.toList EM) 0I
    let c = rsaep m key
    let C = i2osp c k
    C

