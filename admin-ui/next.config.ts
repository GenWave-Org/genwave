import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone",
  // Proxy API requests to the C# backend at runtime
  // The BACKEND_URL env var is set in compose; in dev it falls back to localhost
  async rewrites() {
    const backendUrl = process.env["BACKEND_URL"] ?? "http://localhost:5000";
    return [
      {
        source: "/api/:path*",
        destination: `${backendUrl}/api/:path*`,
      },
    ];
  },
};

export default nextConfig;
