FROM node:24-alpine
WORKDIR /app
ENV HOST=0.0.0.0
COPY package.json ./
COPY src ./src
USER node
EXPOSE 3000
CMD ["node", "src/index.js"]
