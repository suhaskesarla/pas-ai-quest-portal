FROM node:24-alpine AS build
WORKDIR /app
ARG VITE_DEMO_AUTH_ENABLED=false
ENV VITE_DEMO_AUTH_ENABLED=${VITE_DEMO_AUTH_ENABLED}
COPY src/web/package*.json ./
RUN npm ci
COPY src/web/ ./
RUN npm run build

FROM nginx:1.29-alpine
COPY src/web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
