FROM node:24-alpine AS build
WORKDIR /app
COPY src/web/package*.json ./
RUN npm ci
COPY src/web/ ./
RUN npm run build

FROM nginx:1.29-alpine
COPY src/web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
