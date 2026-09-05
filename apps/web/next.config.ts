import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  images: {
    // TMDb serves poster art from its own CDN. Next.js requires remote hosts to
    // be allowlisted explicitly so a compromised or attacker-supplied URL cannot
    // turn the image optimiser into an open proxy for arbitrary hosts.
    remotePatterns: [
      {
        protocol: "https",
        hostname: "image.tmdb.org",
        pathname: "/t/p/**",
      },
    ],
  },
};

export default nextConfig;
