# ---------------------------------------------------------------- build stage
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# Vite inlines env vars at BUILD time, not at runtime. An image built for one
# host cannot be repointed by restarting it with a different variable, which is
# why this arrives as a build argument rather than an environment setting.
ARG VITE_API_BASE_URL=http://localhost:8080
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL

RUN npm run build

# -------------------------------------------------------------- runtime stage
# Static files need a static file server, not Node.
FROM nginx:1.27-alpine AS runtime

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80

# 127.0.0.1, not localhost: /etc/hosts maps localhost to both 127.0.0.1 and ::1,
# busybox wget tries the IPv6 address first, and nginx listens on IPv4 only — so
# "localhost" here is a connection refused against a perfectly healthy server.
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s \
    CMD wget --spider -q http://127.0.0.1/ || exit 1
