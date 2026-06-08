import type { NextConfig } from "next";

const API_GATEWAY_URL = process.env.API_GATEWAY_URL ?? "http://api-gateway:8000";

const nextConfig: NextConfig = {
  output: "standalone",
  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${API_GATEWAY_URL}/api/:path*`,
      },
    ];
  },
};

export default nextConfig;
